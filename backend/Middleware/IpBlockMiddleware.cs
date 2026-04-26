using FinanceManagement.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceManagement.API.Middleware;

// Returns 403 to any request whose client IP is in BlockedIps with
// ExpiresAt > now. Caches results per-IP for 60 seconds to avoid hitting
// Postgres on every request.
//
// Runs early in the pipeline (before rate limiter) so blocked IPs don't
// waste any auth/rate-limit work. The Retry-After header is intentionally
// generic — clients see "try again in an hour" even though the actual
// block lasts 24h, to avoid revealing exact block durations to attackers.
public sealed class IpBlockMiddleware
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IpBlockMiddleware> _logger;

    public IpBlockMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<IpBlockMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Connection.RemoteIpAddress is the canonical IP — UseForwardedHeaders
        // (registered first in Program.cs) rewrites it from X-Forwarded-For
        // when the immediate peer is a trusted proxy.
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var blocked = await IsBlockedAsync(ip, context);
        if (blocked)
        {
            _logger.LogInformation("Blocked request from {Ip}", ip);
            context.Response.StatusCode = 403;
            context.Response.Headers["Retry-After"] = "3600";
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        await _next(context);
    }

    private async Task<bool> IsBlockedAsync(string ip, HttpContext context)
    {
        var key = $"ip-block:{ip}";
        if (_cache.TryGetValue<bool>(key, out var cached))
        {
            return cached;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var blocked = await db.BlockedIps.AnyAsync(b => b.IpAddress == ip && b.ExpiresAt > now);

        _cache.Set(key, blocked, CacheTtl);
        return blocked;
    }
}
