using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Data;
using FinanceManagement.API.Middleware;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

app.MapControllers();

app.Run();
