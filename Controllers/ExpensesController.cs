using Beauty.Api.Data;
using Beauty.Api.Models.Expenses;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly IWebhookService _webhook;
    private readonly PowerAutomateSettings _pa;

    public ExpensesController(
        BeautyDbContext db,
        IWebhookService webhook,
        IOptions<PowerAutomateSettings> pa)
    {
        _db      = db;
        _webhook = webhook;
        _pa      = pa.Value;
    }

    // ── POST /api/expenses ─────────────────────────────────────────────
    // Any authenticated team member can submit an expense

    public record SubmitExpenseReq(
        ExpenseCategory Category,
        int             AmountCents,
        string          Description,
        DateTime        ExpenseDate,
        string?         ReceiptUrl);

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitExpenseReq req)
    {
        if (req.AmountCents <= 0)
            return BadRequest(new { error = "Amount must be greater than zero." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var name   = User.FindFirst(ClaimTypes.Name)?.Value
                  ?? User.FindFirst(ClaimTypes.Email)?.Value
                  ?? "Team Member";

        var expense = new Expense
        {
            SubmittedByUserId = userId,
            SubmittedByName   = name,
            Category          = req.Category,
            AmountCents       = req.AmountCents,
            Description       = req.Description,
            ExpenseDate       = req.ExpenseDate,
            ReceiptUrl        = req.ReceiptUrl,
            Status            = ExpenseStatus.Pending
        };

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();

        _ = _webhook.FireAsync(_pa.ExpenseSubmittedUrl, new
        {
            event_type    = "expense.submitted",
            expense_id    = expense.Id,
            submitted_by  = expense.SubmittedByName,
            category      = expense.Category.ToString(),
            amount_dollars = Math.Round(expense.AmountCents / 100m, 2),
            description   = expense.Description,
            expense_date  = expense.ExpenseDate.ToString("yyyy-MM-dd"),
            submitted_at  = expense.CreatedAt
        });

        return Ok(Map(expense));
    }

    // ── GET /api/expenses ──────────────────────────────────────────────
    // Admin sees all; team members see only their own

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string?          status,
        [FromQuery] ExpenseCategory? category,
        [FromQuery] int?             year,
        [FromQuery] int?             month)
    {
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Staff");
        var userId  = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var query = _db.Expenses.AsNoTracking().AsQueryable();

        if (!isAdmin)
            query = query.Where(e => e.SubmittedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ExpenseStatus>(status, true, out var parsedStatus))
            query = query.Where(e => e.Status == parsedStatus);

        if (category.HasValue)
            query = query.Where(e => e.Category == category.Value);

        if (year.HasValue)
            query = query.Where(e => e.ExpenseDate.Year == year.Value);

        if (month.HasValue)
            query = query.Where(e => e.ExpenseDate.Month == month.Value);

        var results = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
        return Ok(results.Select(Map));
    }

    // ── GET /api/expenses/{id} ─────────────────────────────────────────

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var userId  = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Staff");

        var expense = await _db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        if (!isAdmin && expense.SubmittedByUserId != userId) return Forbid();

        return Ok(Map(expense));
    }

    // ── PUT /api/expenses/{id}/review ──────────────────────────────────
    // Admin approve or reject

    public record ReviewReq(ExpenseStatus Status, string? Notes);

    [HttpPut("{id:long}/review")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Review(long id, [FromBody] ReviewReq req)
    {
        if (req.Status == ExpenseStatus.Pending)
            return BadRequest(new { error = "Review status must be Approved or Rejected." });

        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();

        expense.Status          = req.Status;
        expense.ReviewNotes     = req.Notes;
        expense.ReviewedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        expense.ReviewedByName  = User.FindFirst(ClaimTypes.Name)?.Value
                                ?? User.FindFirst(ClaimTypes.Email)?.Value;
        expense.ReviewedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(Map(expense));
    }

    // ── DELETE /api/expenses/{id} ──────────────────────────────────────
    // Submitter can delete their own pending expense; Admin can delete any

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId  = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin");

        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();
        if (!isAdmin && expense.SubmittedByUserId != userId) return Forbid();
        if (!isAdmin && expense.Status != ExpenseStatus.Pending)
            return BadRequest(new { error = "Only pending expenses can be deleted by the submitter." });

        _db.Expenses.Remove(expense);
        await _db.SaveChangesAsync();
        return Ok(new { id, deleted = true });
    }

    // ── GET /api/expenses/summary ──────────────────────────────────────
    // Monthly totals by category — used in Monthly Financial Summary

    [HttpGet("summary")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Summary([FromQuery] int? year, [FromQuery] int? month)
    {
        var y = year  ?? DateTime.UtcNow.Year;
        var m = month ?? DateTime.UtcNow.Month;

        var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddMonths(1);

        var approved = await _db.Expenses
            .AsNoTracking()
            .Where(e => e.Status == ExpenseStatus.Approved
                     && e.ExpenseDate >= start
                     && e.ExpenseDate <  end)
            .ToListAsync();

        var pending = await _db.Expenses
            .AsNoTracking()
            .Where(e => e.Status == ExpenseStatus.Pending
                     && e.ExpenseDate >= start
                     && e.ExpenseDate <  end)
            .ToListAsync();

        var byCategory = approved
            .GroupBy(e => e.Category.ToString())
            .Select(g => new
            {
                Category    = g.Key,
                TotalCents  = g.Sum(e => e.AmountCents),
                TotalDollars = Math.Round(g.Sum(e => e.AmountCents) / 100m, 2),
                Count       = g.Count()
            })
            .OrderByDescending(g => g.TotalCents)
            .ToList();

        return Ok(new
        {
            Period              = start.ToString("MMMM yyyy"),
            ApprovedTotalCents  = approved.Sum(e => e.AmountCents),
            ApprovedTotal       = Math.Round(approved.Sum(e => e.AmountCents) / 100m, 2),
            PendingTotalCents   = pending.Sum(e => e.AmountCents),
            PendingTotal        = Math.Round(pending.Sum(e => e.AmountCents) / 100m, 2),
            ByCategory          = byCategory,
            ApprovedCount       = approved.Count,
            PendingCount        = pending.Count
        });
    }

    // ── GET /api/expenses/categories ──────────────────────────────────

    [HttpGet("categories")]
    public IActionResult GetCategories() =>
        Ok(Enum.GetValues<ExpenseCategory>().Select(c => new
        {
            Value = (int)c,
            Name  = c.ToString()
        }));

    // ── Mapper ─────────────────────────────────────────────────────────

    private static object Map(Expense e) => new
    {
        e.Id,
        e.SubmittedByName,
        e.SubmittedByUserId,
        Category       = e.Category.ToString(),
        e.AmountCents,
        AmountDollars  = Math.Round(e.AmountCents / 100m, 2),
        e.Description,
        e.ExpenseDate,
        e.ReceiptUrl,
        Status         = e.Status.ToString(),
        e.ReviewedByName,
        e.ReviewedAt,
        e.ReviewNotes,
        e.CreatedAt
    };
}
