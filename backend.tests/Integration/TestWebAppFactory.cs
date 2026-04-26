using System.Security.Claims;
using FinanceManagement.API.Data;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagement.API.Tests.Integration;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public RecordingEmailSender Sender { get; } = new();
    public StubFirebaseLookup Lookup { get; } = new();
    public string TestUserId { get; set; } = "test-user-1";

    public TestWebAppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Replace Postgres DbContext with in-memory SQLite for the test.
            // Program.cs skips the Npgsql registration in the Testing
            // environment, but defensively remove any existing options
            // descriptor so the SQLite registration is the only one.
            var pgDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (pgDescriptor is not null) services.Remove(pgDescriptor);
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));

            // Replace IEmailSender with the recording test double.
            var senderDescriptor = services.Single(d => d.ServiceType == typeof(IEmailSender));
            services.Remove(senderDescriptor);
            services.AddSingleton<IEmailSender>(Sender);

            // Replace Firebase user-lookup so we don't need the real SDK in tests.
            var lookupDescriptor = services.Single(d => d.ServiceType == typeof(IFirebaseUserLookup));
            services.Remove(lookupDescriptor);
            services.AddSingleton<IFirebaseUserLookup>(Lookup);

            // Ensure schema is created on the SQLite connection.
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            services.AddSingleton<IStartupFilter>(new TestAuthStartupFilter(this));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _connection.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class StubFirebaseLookup : IFirebaseUserLookup
{
    public HashSet<string> RegisteredEmails { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<FirebaseUserInfo?> LookupAsync(string email, CancellationToken ct)
        => Task.FromResult(RegisteredEmails.Contains(email) ? new FirebaseUserInfo("Test User") : null);
}

public sealed class TestAuthStartupFilter : IStartupFilter
{
    private readonly TestWebAppFactory _factory;

    public TestAuthStartupFilter(TestWebAppFactory factory) => _factory = factory;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (ctx, n) =>
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim("firebase_uid", _factory.TestUserId),
                }, "Test");
                ctx.User = new ClaimsPrincipal(identity);
                await n();
            });
            next(app);
        };
    }
}
