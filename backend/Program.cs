using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
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

// Forwarded-headers config — when this app sits behind a trusted proxy
// (the nginx container in the Docker bridge network in prod), incoming
// X-Forwarded-For values are used to populate Connection.RemoteIpAddress.
// When the immediate peer is NOT in the trusted-proxies list, the header
// is ignored, preventing an attacker who reaches the backend directly
// from spoofing IPs and rotating the rate-limit / IP-block partition key.
//
// In Testing the test factory's HttpClient connects to TestServer with
// no real network hop, so we trust everything to let test-supplied
// X-Forwarded-For values flow through.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
    else
    {
        // Default Docker bridge networks (172.16.0.0/12 and 10.0.0.0/8)
        // cover any nginx container co-deployed via docker-compose. Loopback
        // is also trusted for local non-Docker dev runs.
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
    }
});

// Rate limiter — partition by Firebase UID when authenticated, fall back
// to client IP. RemoteIpAddress is the canonical IP source: UseForwardedHeaders
// (registered first in the pipeline) rewrites it from X-Forwarded-For
// when the immediate peer is in the trusted-proxies list, and leaves it
// as the actual TCP peer otherwise.
static string ResolvePartitionKey(HttpContext ctx)
{
    var uid = ctx.User.FindFirst("firebase_uid")?.Value;
    if (!string.IsNullOrEmpty(uid)) return $"uid:{uid}";

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
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

builder.Services.AddScoped<ILockoutService, LockoutService>();
builder.Services.AddScoped<IPeriodSummaryService, PeriodSummaryService>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(capacity: 1000));
builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.AddScoped<ILockoutEscalationDetector, LockoutEscalationDetector>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddSingleton<IFirebaseUserLookup, FirebaseUserLookup>();

var resendApiKey       = Environment.GetEnvironmentVariable("RESEND_API_KEY");
var resendFromAddress  = Environment.GetEnvironmentVariable("RESEND_FROM_ADDRESS") ?? "noreply@manhou.de";
var adminAlertEmail    = Environment.GetEnvironmentVariable("ADMIN_ALERT_EMAIL") ?? "wenhaoyuan02@gmail.com";
var resetPasswordUrl   = Environment.GetEnvironmentVariable("RESET_PASSWORD_URL") ?? "https://finance.manhou.de/forgot-password";
var alertsEnabled      = (Environment.GetEnvironmentVariable("LOCKOUT_ALERTS_ENABLED") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton(new DispatcherOptions(adminAlertEmail, resetPasswordUrl));

if (alertsEnabled && !string.IsNullOrWhiteSpace(resendApiKey))
{
    builder.Services.AddHttpClient(nameof(ResendEmailSender))
        .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.resend.com/"));
    builder.Services.AddSingleton<IEmailSender>(sp =>
        new ResendEmailSender(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ResendEmailSender)),
            resendApiKey,
            resendFromAddress,
            sp.GetRequiredService<ILogger<ResendEmailSender>>()));
    Console.WriteLine("INFO: Resend email sender registered.");
}
else
{
    builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();
    Console.WriteLine("INFO: NoOpEmailSender registered (RESEND_API_KEY missing or LOCKOUT_ALERTS_ENABLED=false).");
}

// In the Testing environment the integration test factory swaps in an
// in-memory SQLite provider — registering Npgsql here would cause EF to
// see two providers in the same service container and fail at startup.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
}

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

// Skip Firebase init in the Testing environment — `FirebaseApp.Create` is a
// static singleton, so the second integration test would throw
// "default FirebaseApp already exists" when WebApplicationFactory rebuilds
// the host. The `IFirebaseUserLookup` test double bypasses the SDK entirely.
if (!builder.Environment.IsEnvironment("Testing"))
{
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

// Skip migrations in the Testing environment — the integration test
// factory uses SQLite + EnsureCreated() and the Postgres-typed migrations
// would fail against SQLite.
if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// MUST be first — rewrites Connection.RemoteIpAddress from X-Forwarded-For
// when the immediate peer is a trusted proxy. Every downstream component
// that reads the client IP (rate limiter, IpBlockMiddleware, controllers)
// depends on this having run already.
app.UseForwardedHeaders();
app.UseCors("AllowAngular");
app.UseHttpsRedirection();
app.UseMiddleware<IpBlockMiddleware>();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<FirebaseAuthMiddleware>();
}
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
