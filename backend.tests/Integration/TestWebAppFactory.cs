using FinanceManagement.API.Data;
using FinanceManagement.API.Services;
using FinanceManagement.API.Tests.TestHelpers;
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
