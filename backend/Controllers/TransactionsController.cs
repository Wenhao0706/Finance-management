using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Data;
using FinanceManagement.API.Models;
using FinanceManagement.API.Services;

namespace FinanceManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPeriodSummaryService _summaryService;

    public TransactionsController(AppDbContext db, IPeriodSummaryService summaryService)
    {
        _db = db;
        _summaryService = summaryService;
    }

    private string? CurrentUserId => User.FindFirst("firebase_uid")?.Value;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transaction>>> GetAll()
    {
        if (CurrentUserId is null) return Unauthorized();

        return await _db.Transactions
            .Where(t => t.UserId == CurrentUserId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Transaction>> Get(int id)
    {
        if (CurrentUserId is null) return Unauthorized();

        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);

        return transaction is null ? NotFound() : transaction;
    }

    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<Transaction>> Create(Transaction transaction)
    {
        if (CurrentUserId is null) return Unauthorized();

        transaction.UserId = CurrentUserId;
        transaction.CreatedAt = DateTime.UtcNow;
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = transaction.Id }, transaction);
    }

    [HttpPut("{id}")]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Update(int id, Transaction transaction)
    {
        if (CurrentUserId is null) return Unauthorized();
        if (id != transaction.Id) return BadRequest();

        var existing = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);

        if (existing is null) return NotFound();

        existing.Description = transaction.Description;
        existing.Amount = transaction.Amount;
        existing.Type = transaction.Type;
        existing.Category = transaction.Category;
        existing.Date = transaction.Date;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [EnableRateLimiting("writes")]
    public async Task<IActionResult> Delete(int id)
    {
        if (CurrentUserId is null) return Unauthorized();

        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);

        if (transaction is null) return NotFound();

        _db.Transactions.Remove(transaction);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PeriodSummary>> GetSummary(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();

        if (year.HasValue && (year.Value < 2000 || year.Value > 2100))
            return BadRequest(new { error = "Year must be between 2000 and 2100." });
        if (month.HasValue && (month.Value < 1 || month.Value > 12))
            return BadRequest(new { error = "Month must be 1-12." });
        if (month.HasValue && !year.HasValue)
            return BadRequest(new { error = "Year is required when month is provided." });

        var now = DateTime.UtcNow;
        var resolvedYear = year ?? now.Year;
        var resolvedMonth = month ?? (year.HasValue ? (int?)null : now.Month);

        if (resolvedMonth.HasValue)
        {
            var summary = await _summaryService.GetMonthlyAsync(CurrentUserId, resolvedYear, resolvedMonth.Value, ct);
            return Ok(summary);
        }
        else
        {
            var summary = await _summaryService.GetYearlyAsync(CurrentUserId, resolvedYear, ct);
            return Ok(summary);
        }
    }
}
