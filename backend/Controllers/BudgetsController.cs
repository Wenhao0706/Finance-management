using FinanceManagement.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinanceManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BudgetsController : ControllerBase
{
    private readonly IBudgetService _svc;

    public BudgetsController(IBudgetService svc) => _svc = svc;

    private string? CurrentUserId => User.FindFirst("firebase_uid")?.Value;

    [HttpGet("{year:int}/{month:int}")]
    public async Task<ActionResult<BudgetSnapshot>> Get(int year, int month, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        if (year < 2000 || year > 2100) return BadRequest(new { error = "Year must be 2000-2100." });
        if (month < 1 || month > 12) return BadRequest(new { error = "Month must be 1-12." });

        var snapshot = await _svc.GetSnapshotAsync(CurrentUserId, year, month, ct);
        return Ok(snapshot);
    }

    [HttpPut("{year:int}/{month:int}")]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<BudgetSnapshot>> Update(int year, int month, [FromBody] BudgetUpdate update, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        if (year < 2000 || year > 2100) return BadRequest(new { error = "Year must be 2000-2100." });
        if (month < 1 || month > 12) return BadRequest(new { error = "Month must be 1-12." });

        try
        {
            var snapshot = await _svc.UpdateBudgetAsync(CurrentUserId, year, month, update, ct);
            return Ok(snapshot);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

[ApiController]
[Route("api/category-budgets")]
public class CategoryBudgetsController : ControllerBase
{
    private readonly IBudgetService _svc;

    public CategoryBudgetsController(IBudgetService svc) => _svc = svc;

    private string? CurrentUserId => User.FindFirst("firebase_uid")?.Value;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryBudgetEntry>>> List(CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        var entries = await _svc.GetCategoryBudgetsAsync(CurrentUserId, ct);
        return Ok(entries);
    }

    [HttpPut("{categoryId:int}")]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<CategoryBudgetEntry>> Update(int categoryId, [FromBody] CategoryBudgetUpdate update, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();

        try
        {
            var entry = await _svc.UpdateCategoryBudgetAsync(CurrentUserId, categoryId, update, ct);
            return Ok(entry);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
