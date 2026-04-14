using FinanceManagement.API.Services;

namespace FinanceManagement.API.Middleware;

// Validates the X-Firebase-AppCheck header on every authenticated request.
// Runs AFTER FirebaseAuthMiddleware so structured logs can include the Firebase UID.
//
// Two operating modes controlled by APPCHECK_ENFORCE env var:
//   false (default) — log-only. Every verification failure is logged but the
//                      request still passes through. Used during initial
//                      rollout to make sure real clients aren't rejected.
//   true            — enforce. Verification failure → 401 Authentication required.
//
// Flip to enforce mode only after 48h of clean logs (no false positives from
// real users). Public paths (/healthz, /openapi, /swagger) are exempted — same
// list as FirebaseAuthMiddleware.
public class AppCheckMiddleware
{
    private static readonly string[] PublicPathPrefixes =
    {
        "/healthz",
        "/openapi",
        "/swagger",
    };

    private readonly RequestDelegate _next;
    private readonly IAppCheckTokenVerifier _verifier;
    private readonly ILogger<AppCheckMiddleware> _logger;
    private readonly bool _enforce;

    public AppCheckMiddleware(
        RequestDelegate next,
        IAppCheckTokenVerifier verifier,
        IConfiguration configuration,
        ILogger<AppCheckMiddleware> logger)
    {
        _next = next;
        _verifier = verifier;
        _logger = logger;
        _enforce = ReadEnforceFlag(configuration);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (PublicPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers["X-Firebase-AppCheck"].FirstOrDefault();
        var result = await _verifier.VerifyAsync(token);

        if (!result.IsValid)
        {
            var uid = context.User.FindFirst("firebase_uid")?.Value ?? "anonymous";
            _logger.LogWarning(
                "AppCheck verification failed (uid={Uid}, path={Path}, reason={Reason}, mode={Mode})",
                uid,
                path,
                result.FailureReason,
                _enforce ? "enforce" : "log-only");

            if (_enforce)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Authentication required.");
                return;
            }
        }

        await _next(context);
    }

    private static bool ReadEnforceFlag(IConfiguration configuration)
    {
        var envVar = Environment.GetEnvironmentVariable("APPCHECK_ENFORCE");
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            return string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase);
        }

        return configuration.GetValue<bool>("AppCheck:Enforce");
    }
}
