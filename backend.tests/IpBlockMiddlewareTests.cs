using System.Net;
using FinanceManagement.API.Data;
using FinanceManagement.API.Middleware;
using FinanceManagement.API.Models;
using FinanceManagement.API.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagement.API.Tests;

public class IpBlockMiddlewareTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();

    public void Dispose() => _dbFactory.Dispose();

    // Sets Connection.RemoteIpAddress directly — in production
    // UseForwardedHeaders rewrites RemoteIpAddress from X-Forwarded-For
    // before the middleware runs, so testing against RemoteIpAddress is
    // the right unit-test boundary.
    private static HttpContext BuildContext(string ip)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return ctx;
    }

    [Fact]
    public async Task Active_Block_Returns_403()
    {
        using var db = _dbFactory.Create();
        db.BlockedIps.Add(new BlockedIp
        {
            IpAddress = "1.2.3.4",
            Reason = "ProbingMultipleEmails",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpBlockMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<IpBlockMiddleware>.Instance);

        var ctx = BuildContext("1.2.3.4");
        ctx.RequestServices = BuildServiceProvider(db);

        await middleware.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Expired_Block_Falls_Through()
    {
        using var db = _dbFactory.Create();
        db.BlockedIps.Add(new BlockedIp
        {
            IpAddress = "1.2.3.4",
            Reason = "ProbingMultipleEmails",
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new IpBlockMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<IpBlockMiddleware>.Instance);

        var ctx = BuildContext("1.2.3.4");
        ctx.RequestServices = BuildServiceProvider(db);

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Unblocked_Ip_Falls_Through()
    {
        using var db = _dbFactory.Create();

        var nextCalled = false;
        var middleware = new IpBlockMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<IpBlockMiddleware>.Instance);

        var ctx = BuildContext("8.8.8.8");
        ctx.RequestServices = BuildServiceProvider(db);

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    private static IServiceProvider BuildServiceProvider(AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        return services.BuildServiceProvider();
    }
}
