using Beauty.Api.Data;
using Beauty.Api.Models.Expenses;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Beauty.Api.Controllers;

// ── Power Automate action callbacks ───────────────────────────────────────────
// These endpoints are called by Power Automate after Kenny interacts with a
// Teams adaptive card. They use a shared webhook secret instead of a user JWT.

[ApiController]
[Route("api/power-automate")]
[AllowAnonymous]
public class PowerAutomateController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly PowerAutomateSettings _pa;

    public PowerAutomateController(BeautyDbContext db, IOptions<PowerAutomateSettings> pa)
    {
        _db = db;
        _pa = pa.Value;
    }

    private bool ValidSecret() =>
        Request.Headers.TryGetValue("X-Webhook-Secret", out var v) &&
        v.ToString() == _pa.WebhookSecret &&
        !string.IsNullOrWhiteSpace(_pa.WebhookSecret);

    // ── POST /api/power-automate/expenses/{id}/approve ────────────────
    // PA calls this when Kenny clicks Approve in Teams

    [HttpPost("expenses/{id:long}/approve")]
    public async Task<IActionResult> ApproveExpense(long id, [FromBody] CallbackReq req)
    {
        if (!ValidSecret()) return Unauthorized(new { error = "Invalid webhook secret." });

        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound(new { error = "Expense not found." });

        if (expense.Status != ExpenseStatus.Pending)
            return BadRequest(new { error = $"Expense is already {expense.Status}." });

        expense.Status           = ExpenseStatus.Approved;
        expense.ReviewedByName   = req.ReviewedBy ?? "Kenny Stephen";
        expense.ReviewNotes      = req.Notes;
        expense.ReviewedAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { id, status = "Approved", reviewedAt = expense.ReviewedAt });
    }

    // ── POST /api/power-automate/expenses/{id}/reject ─────────────────
    // PA calls this when Kenny clicks Reject in Teams

    [HttpPost("expenses/{id:long}/reject")]
    public async Task<IActionResult> RejectExpense(long id, [FromBody] CallbackReq req)
    {
        if (!ValidSecret()) return Unauthorized(new { error = "Invalid webhook secret." });

        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound(new { error = "Expense not found." });

        if (expense.Status != ExpenseStatus.Pending)
            return BadRequest(new { error = $"Expense is already {expense.Status}." });

        expense.Status           = ExpenseStatus.Rejected;
        expense.ReviewedByName   = req.ReviewedBy ?? "Kenny Stephen";
        expense.ReviewNotes      = req.Notes;
        expense.ReviewedAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { id, status = "Rejected", reviewedAt = expense.ReviewedAt });
    }

    public record CallbackReq(string? ReviewedBy, string? Notes);
}
