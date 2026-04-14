using System.Security.Claims;
using FirebaseAdmin.Auth;

namespace FinanceManagement.API.Middleware;

public class FirebaseAuthMiddleware
{
    private static readonly string[] PublicPathPrefixes =
    {
        "/healthz",
        "/openapi",
        "/swagger",
        "/api/auth-events",  // anonymous lockout tracking — see AuthEventsController
    };

    private readonly RequestDelegate _next;

    public FirebaseAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (PublicPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (authHeader is null || !authHeader.StartsWith("Bearer "))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var token = authHeader["Bearer ".Length..];

        try
        {
            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, decodedToken.Uid),
                new(ClaimTypes.Email, decodedToken.Claims.GetValueOrDefault("email")?.ToString() ?? ""),
                new("firebase_uid", decodedToken.Uid),
            };

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Firebase"));
            await _next(context);
        }
        catch (FirebaseAuthException)
        {
            await WriteUnauthorizedAsync(context);
        }
    }

    // Generic 401 — never reveal whether the header was missing, malformed, or the token was bad.
    // Discriminating helps attackers probe the auth mechanism.
    private static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = 401;
        return context.Response.WriteAsync("Authentication required.");
    }
}
