using FinanceManagement.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinanceManagement.API.Controllers;

// Anonymous endpoints — the frontend reports each Firebase login attempt
// here so we can run the progressive-lockout policy and show the user a
// precise countdown. Both endpoints are aggressively rate-limited (see the
// "authEvents" policy in Program.cs) because they take any email value
// without authentication.
[ApiController]
[Route("api/auth-events")]
[EnableRateLimiting("authEvents")]
public class AuthEventsController : ControllerBase
{
    private readonly ILockoutService _lockout;

    public AuthEventsController(ILockoutService lockout) => _lockout = lockout;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] AuthEventDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            return BadRequest(new { error = "Email is required." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = HttpContext.Request.Headers.UserAgent.FirstOrDefault();

        await _lockout.RecordAsync(dto.Email, ip, dto.Success, ua, dto.ErrorCode, cancellationToken);

        return Ok();
    }

    [HttpGet("check")]
    public async Task<ActionResult<LockoutDecision>> Check([FromQuery] string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "Email is required." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var decision = await _lockout.CheckAsync(email, ip, cancellationToken);
        return Ok(decision);
    }
}

public record AuthEventDto(string Email, bool Success, string? ErrorCode);
