using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Data;
using FinanceManagement.API.Middleware;
using FinanceManagement.API.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Rate limiter — partition by Firebase UID when authenticated, fall back to
// client IP (prefer X-Forwarded-For since we sit behind nginx + Cloudflare Tunnel).
// Global default covers reads; named "writes" policy is applied via
// [EnableRateLimiting("writes")] on POST/PUT/DELETE endpoints.
static string ResolvePartitionKey(HttpContext ctx)
{
    var uid = ctx.User.FindFirst("firebase_uid")?.Value;
    if (!string.IsNullOrEmpty(uid)) return $"uid:{uid}";

    var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        return $"ip:{forwarded.Split(',')[0].Trim()}";
    }

    return $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        return ValueTask.CompletedTask;
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ResolvePartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
        }));

    options.AddPolicy("writes", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ResolvePartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
        }));

    // Anonymous lockout-tracking endpoints. Strict per-IP cap because anyone
    // can hit them — without rate limiting, an attacker could flood
    // /api/auth-events with bogus events to grow the LoginAttempts table.
    options.AddPolicy("authEvents", ctx =>
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ip = !string.IsNullOrWhiteSpace(fwd)
            ? fwd.Split(',')[0].Trim()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

builder.Services.AddScoped<ILockoutService, LockoutService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Initialize Firebase Admin SDK.
// Source priority:
//   1. FIREBASE_SERVICE_ACCOUNT_JSON env var containing base64-encoded JSON (hosted).
//   2. firebase-service-account.json file on disk (local dev).
var serviceAccountB64 = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");
var serviceAccountPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json");

if (!string.IsNullOrWhiteSpace(serviceAccountB64))
{
    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(serviceAccountB64));
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromJson(json),
    });
}
else if (File.Exists(serviceAccountPath))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(serviceAccountPath),
    });
}
else
{
    Console.WriteLine("WARNING: Firebase credentials missing. Set FIREBASE_SERVICE_ACCOUNT_JSON env var (base64) or place firebase-service-account.json at the app root. Auth middleware will fail.");
}

// App Check wiring — only registered when the project number and app ID are
// known. Without them the middleware is skipped entirely so existing deploys
// don't break on rollout. Add APPCHECK_ENFORCE=true once logs show no
// false positives.
var firebaseProjectNumber = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_NUMBER")
    ?? builder.Configuration["Firebase:ProjectNumber"];
var firebaseAppId = Environment.GetEnvironmentVariable("FIREBASE_APP_ID")
    ?? builder.Configuration["Firebase:AppId"];
var appCheckConfigured = !string.IsNullOrWhiteSpace(firebaseProjectNumber)
    && !string.IsNullOrWhiteSpace(firebaseAppId);

if (appCheckConfigured)
{
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IAppCheckTokenVerifier>(sp =>
        new AppCheckTokenVerifier(
            firebaseProjectNumber!,
            firebaseAppId!,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<AppCheckTokenVerifier>>()));
}
else
{
    Console.WriteLine("INFO: App Check not configured (FIREBASE_PROJECT_NUMBER and/or FIREBASE_APP_ID missing). Skipping App Check middleware.");
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAngular");
app.UseHttpsRedirection();
app.UseMiddleware<FirebaseAuthMiddleware>();
if (appCheckConfigured)
{
    app.UseMiddleware<AppCheckMiddleware>();
}
app.UseRateLimiter();

// Public liveness probe — no auth, no rate limit, no timestamp (timestamps leak server time
// and aren't needed for orchestration health checks).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapControllers();

app.Run();
