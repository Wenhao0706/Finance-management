using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using FinanceManagement.API.Data;
using FinanceManagement.API.Models;

namespace FinanceManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db) => _db = db;

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
    public async Task<ActionResult<object>> GetSummary()
    {
        if (CurrentUserId is null) return Unauthorized();

        var userTransactions = _db.Transactions.Where(t => t.UserId == CurrentUserId);
        var totalIncome = await userTransactions.Where(t => t.Type == "income").SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var totalExpense = await userTransactions.Where(t => t.Type == "expense").SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return new
        {
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Balance = totalIncome - totalExpense
        };
    }
}
