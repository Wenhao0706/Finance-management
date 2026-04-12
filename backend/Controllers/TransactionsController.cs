using Microsoft.AspNetCore.Mvc;
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transaction>>> GetAll()
    {
        return await _db.Transactions.OrderByDescending(t => t.Date).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Transaction>> Get(int id)
    {
        var transaction = await _db.Transactions.FindAsync(id);
        return transaction is null ? NotFound() : transaction;
    }

    [HttpPost]
    public async Task<ActionResult<Transaction>> Create(Transaction transaction)
    {
        transaction.CreatedAt = DateTime.UtcNow;
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = transaction.Id }, transaction);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Transaction transaction)
    {
        if (id != transaction.Id) return BadRequest();

        _db.Entry(transaction).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var transaction = await _db.Transactions.FindAsync(id);
        if (transaction is null) return NotFound();

        _db.Transactions.Remove(transaction);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<object>> GetSummary()
    {
        var transactions = await _db.Transactions.ToListAsync();
        var totalIncome = transactions.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = transactions.Where(t => t.Type == "expense").Sum(t => t.Amount);

        return new
        {
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Balance = totalIncome - totalExpense
        };
    }
}
