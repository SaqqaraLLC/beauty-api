using Beauty.Api.Data;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/enterprise")]
[Authorize(Roles = "Admin")]
public class EnterpriseController : ControllerBase
{
    private readonly BeautyDbContext _db;

    public EnterpriseController(BeautyDbContext db) => _db = db;

    // ── GET /api/enterprise/tiers ──────────────────────────────────────────

    [HttpGet("tiers")]
    [AllowAnonymous]
    public IActionResult GetTiers() =>
        Ok(EnterpriseTier.All.Select(t => new
        {
            t.Name,
            t.SeatLimit,
            t.MaxMonthlyBookings,
            t.MonthlyPrice,
            t.YearlyPrice,
            YearlySavings = t.MonthlyPrice * 12 - t.YearlyPrice
        }));

    // ── GET /api/enterprise ────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var query = _db.EnterpriseAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        var accounts = await query.OrderBy(a => a.Name).ToListAsync();
        return Ok(accounts.Select(MapAccount));
    }

    // ── GET /api/enterprise/{id} ───────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var account = await _db.EnterpriseAccounts
            .AsNoTracking()
            .Include(a => a.ContractHistory.OrderByDescending(h => h.EffectiveDate))
            .FirstOrDefaultAsync(a => a.Id == id);

        if (account is null) return NotFound();
        return Ok(MapAccountDetail(account));
    }

    // ── POST /api/enterprise ───────────────────────────────────────────────

    public record CreateEnterpriseReq(
        string      Name,
        string      PlanTier,
        BillingCycle BillingCycle,
        int?        SeatLimit,
        int?        MaxMonthlyBookings,
        DateTime?   ContractStartDate,
        string?     Notes);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEnterpriseReq req)
    {
        var tier = EnterpriseTier.Get(req.PlanTier);
        if (tier is null)
            return BadRequest(new { error = $"Unknown plan tier '{req.PlanTier}'. Valid: Starter, Growth, Enterprise, Custom." });

        var amount = req.BillingCycle == BillingCycle.Yearly ? tier.YearlyPrice : tier.MonthlyPrice;
        var start  = req.ContractStartDate ?? DateTime.UtcNow;
        var renewal = req.BillingCycle == BillingCycle.Yearly
            ? start.AddYears(1)
            : start.AddMonths(1);

        var account = new EnterpriseAccount
        {
            Name                = req.Name,
            Status              = "Pending",
            PlanTier            = tier.Name,
            BillingCycle        = req.BillingCycle,
            SeatLimit           = req.SeatLimit ?? tier.SeatLimit,
            MaxMonthlyBookings  = req.MaxMonthlyBookings ?? tier.MaxMonthlyBookings,
            ContractAmount      = amount,
            ContractStartDate   = start,
            ContractRenewalDate = renewal
        };

        _db.EnterpriseAccounts.Add(account);

        var history = BuildHistoryEntry(account, "NewContract", null, req.Notes,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value);
        _db.EnterpriseContractHistories.Add(history);

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, MapAccount(account));
    }

    // ── PUT /api/enterprise/{id} ───────────────────────────────────────────

    public record UpdateEnterpriseReq(string Name, string Status);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEnterpriseReq req)
    {
        var account = await _db.EnterpriseAccounts.FindAsync(id);
        if (account is null) return NotFound();

        account.Name   = req.Name;
        account.Status = req.Status;
        await _db.SaveChangesAsync();
        return Ok(MapAccount(account));
    }

    // ── POST /api/enterprise/{id}/renew ───────────────────────────────────
    // Yearly renewal — optionally expand seats or upgrade tier at renewal time

    public record RenewReq(
        string?     NewPlanTier,
        BillingCycle? NewBillingCycle,
        int?        NewSeatLimit,
        int?        NewMaxMonthlyBookings,
        decimal?    CustomAmount,
        string?     Notes);

    [HttpPost("{id:guid}/renew")]
    public async Task<IActionResult> Renew(Guid id, [FromBody] RenewReq req)
    {
        var account = await _db.EnterpriseAccounts.FindAsync(id);
        if (account is null) return NotFound();

        var prevTier   = account.PlanTier;
        var prevSeats  = account.SeatLimit;
        var prevBookings = account.MaxMonthlyBookings;
        var prevAmount = account.ContractAmount;
        var prevCycle  = account.BillingCycle;

        // Apply tier change if requested
        if (!string.IsNullOrWhiteSpace(req.NewPlanTier) && req.NewPlanTier != account.PlanTier)
        {
            var tier = EnterpriseTier.Get(req.NewPlanTier);
            if (tier is null)
                return BadRequest(new { error = $"Unknown plan tier '{req.NewPlanTier}'." });
            account.PlanTier = tier.Name;
            if (req.NewSeatLimit is null)         account.SeatLimit           = tier.SeatLimit;
            if (req.NewMaxMonthlyBookings is null) account.MaxMonthlyBookings  = tier.MaxMonthlyBookings;
        }

        if (req.NewBillingCycle.HasValue)  account.BillingCycle       = req.NewBillingCycle.Value;
        if (req.NewSeatLimit.HasValue)     account.SeatLimit           = req.NewSeatLimit.Value;
        if (req.NewMaxMonthlyBookings.HasValue) account.MaxMonthlyBookings = req.NewMaxMonthlyBookings.Value;

        // Recalculate amount from tier unless a custom amount is provided
        if (req.CustomAmount.HasValue)
        {
            account.ContractAmount = req.CustomAmount.Value;
        }
        else
        {
            var td = EnterpriseTier.Get(account.PlanTier);
            if (td is not null)
                account.ContractAmount = account.BillingCycle == BillingCycle.Yearly ? td.YearlyPrice : td.MonthlyPrice;
        }

        var renewalStart = account.ContractRenewalDate ?? DateTime.UtcNow;
        account.ContractStartDate   = renewalStart;
        account.ContractRenewalDate = account.BillingCycle == BillingCycle.Yearly
            ? renewalStart.AddYears(1)
            : renewalStart.AddMonths(1);
        account.Status = "Active";

        var changeType = prevTier != account.PlanTier ? "Upgrade"
                       : prevSeats != account.SeatLimit ? "SeatExpansion"
                       : prevCycle != account.BillingCycle ? "BillingCycleChange"
                       : "Renewal";

        var history = new EnterpriseContractHistory
        {
            EnterpriseAccountId        = account.Id,
            ChangeType                 = changeType,
            PreviousTier               = prevTier,
            NewTier                    = account.PlanTier,
            PreviousSeatLimit          = prevSeats,
            NewSeatLimit               = account.SeatLimit,
            PreviousMaxMonthlyBookings = prevBookings,
            NewMaxMonthlyBookings      = account.MaxMonthlyBookings,
            PreviousBillingCycle       = prevCycle.ToString(),
            NewBillingCycle            = account.BillingCycle.ToString(),
            PreviousContractAmount     = prevAmount,
            NewContractAmount          = account.ContractAmount,
            EffectiveDate              = renewalStart,
            ExpiryDate                 = account.ContractRenewalDate,
            Notes                      = req.Notes,
            ChangedByUserId            = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ChangedByName              = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value
        };
        _db.EnterpriseContractHistories.Add(history);

        await _db.SaveChangesAsync();
        return Ok(MapAccount(account));
    }

    // ── POST /api/enterprise/{id}/upgrade ─────────────────────────────────
    // Mid-contract tier or seat upgrade

    public record UpgradeReq(
        string?  NewPlanTier,
        int?     NewSeatLimit,
        int?     NewMaxMonthlyBookings,
        decimal? CustomAmount,
        string?  Notes);

    [HttpPost("{id:guid}/upgrade")]
    public async Task<IActionResult> Upgrade(Guid id, [FromBody] UpgradeReq req)
    {
        var account = await _db.EnterpriseAccounts.FindAsync(id);
        if (account is null) return NotFound();

        var prevTier    = account.PlanTier;
        var prevSeats   = account.SeatLimit;
        var prevBookings = account.MaxMonthlyBookings;
        var prevAmount  = account.ContractAmount;

        if (!string.IsNullOrWhiteSpace(req.NewPlanTier))
        {
            var tier = EnterpriseTier.Get(req.NewPlanTier);
            if (tier is null)
                return BadRequest(new { error = $"Unknown plan tier '{req.NewPlanTier}'." });
            account.PlanTier = tier.Name;
            if (req.NewSeatLimit is null)         account.SeatLimit           = tier.SeatLimit;
            if (req.NewMaxMonthlyBookings is null) account.MaxMonthlyBookings  = tier.MaxMonthlyBookings;
        }

        if (req.NewSeatLimit.HasValue)             account.SeatLimit           = req.NewSeatLimit.Value;
        if (req.NewMaxMonthlyBookings.HasValue)     account.MaxMonthlyBookings   = req.NewMaxMonthlyBookings.Value;

        if (req.CustomAmount.HasValue)
        {
            account.ContractAmount = req.CustomAmount.Value;
        }
        else
        {
            var td = EnterpriseTier.Get(account.PlanTier);
            if (td is not null)
                account.ContractAmount = account.BillingCycle == BillingCycle.Yearly ? td.YearlyPrice : td.MonthlyPrice;
        }

        var changeType = prevTier != account.PlanTier ? "Upgrade" : "SeatExpansion";

        var history = new EnterpriseContractHistory
        {
            EnterpriseAccountId        = account.Id,
            ChangeType                 = changeType,
            PreviousTier               = prevTier,
            NewTier                    = account.PlanTier,
            PreviousSeatLimit          = prevSeats,
            NewSeatLimit               = account.SeatLimit,
            PreviousMaxMonthlyBookings = prevBookings,
            NewMaxMonthlyBookings      = account.MaxMonthlyBookings,
            PreviousBillingCycle       = account.BillingCycle.ToString(),
            NewBillingCycle            = account.BillingCycle.ToString(),
            PreviousContractAmount     = prevAmount,
            NewContractAmount          = account.ContractAmount,
            EffectiveDate              = DateTime.UtcNow,
            ExpiryDate                 = account.ContractRenewalDate,
            Notes                      = req.Notes,
            ChangedByUserId            = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ChangedByName              = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value
        };
        _db.EnterpriseContractHistories.Add(history);

        await _db.SaveChangesAsync();
        return Ok(MapAccount(account));
    }

    // ── GET /api/enterprise/{id}/contract-history ─────────────────────────

    [HttpGet("{id:guid}/contract-history")]
    public async Task<IActionResult> GetContractHistory(Guid id)
    {
        if (!await _db.EnterpriseAccounts.AnyAsync(a => a.Id == id)) return NotFound();

        var history = await _db.EnterpriseContractHistories
            .AsNoTracking()
            .Where(h => h.EnterpriseAccountId == id)
            .OrderByDescending(h => h.EffectiveDate)
            .ToListAsync();

        return Ok(history.Select(MapHistory));
    }

    // ── GET /api/enterprise/renewals-due ──────────────────────────────────
    // Accounts renewing within the next N days (default 30)

    [HttpGet("renewals-due")]
    public async Task<IActionResult> GetRenewalsDue([FromQuery] int days = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(days);
        var accounts = await _db.EnterpriseAccounts
            .AsNoTracking()
            .Where(a => a.ContractRenewalDate != null && a.ContractRenewalDate <= cutoff && a.Status == "Active")
            .OrderBy(a => a.ContractRenewalDate)
            .ToListAsync();

        return Ok(accounts.Select(MapAccount));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static EnterpriseContractHistory BuildHistoryEntry(
        EnterpriseAccount account, string changeType,
        string? prevTier, string? notes, string? userId, string? userName) =>
        new()
        {
            EnterpriseAccountId        = account.Id,
            ChangeType                 = changeType,
            PreviousTier               = prevTier,
            NewTier                    = account.PlanTier,
            PreviousSeatLimit          = 0,
            NewSeatLimit               = account.SeatLimit,
            PreviousMaxMonthlyBookings = 0,
            NewMaxMonthlyBookings      = account.MaxMonthlyBookings,
            PreviousBillingCycle       = null,
            NewBillingCycle            = account.BillingCycle.ToString(),
            PreviousContractAmount     = null,
            NewContractAmount          = account.ContractAmount,
            EffectiveDate              = account.ContractStartDate ?? DateTime.UtcNow,
            ExpiryDate                 = account.ContractRenewalDate,
            Notes                      = notes,
            ChangedByUserId            = userId,
            ChangedByName              = userName
        };

    private static object MapAccount(EnterpriseAccount a)
    {
        var tier = EnterpriseTier.Get(a.PlanTier);
        return new
        {
            a.Id,
            a.Name,
            a.Status,
            a.PlanTier,
            BillingCycle       = a.BillingCycle.ToString(),
            EffectiveSeatLimit = a.SeatLimit > 0 ? a.SeatLimit : tier?.SeatLimit ?? 0,
            EffectiveMaxMonthlyBookings = a.MaxMonthlyBookings > 0 ? a.MaxMonthlyBookings : tier?.MaxMonthlyBookings ?? 0,
            a.ContractAmount,
            a.ContractStartDate,
            a.ContractRenewalDate,
            DaysUntilRenewal = a.ContractRenewalDate.HasValue
                ? (int)(a.ContractRenewalDate.Value - DateTime.UtcNow).TotalDays
                : (int?)null,
            a.CreatedAt
        };
    }

    private static object MapAccountDetail(EnterpriseAccount a)
    {
        var base_ = MapAccount(a);
        return new
        {
            ((dynamic)base_).Id,
            ((dynamic)base_).Name,
            ((dynamic)base_).Status,
            ((dynamic)base_).PlanTier,
            ((dynamic)base_).BillingCycle,
            ((dynamic)base_).EffectiveSeatLimit,
            ((dynamic)base_).EffectiveMaxMonthlyBookings,
            ((dynamic)base_).ContractAmount,
            ((dynamic)base_).ContractStartDate,
            ((dynamic)base_).ContractRenewalDate,
            ((dynamic)base_).DaysUntilRenewal,
            ((dynamic)base_).CreatedAt,
            ContractHistory = a.ContractHistory.Select(MapHistory)
        };
    }

    private static object MapHistory(EnterpriseContractHistory h) => new
    {
        h.Id,
        h.ChangeType,
        h.PreviousTier,
        h.NewTier,
        h.PreviousSeatLimit,
        h.NewSeatLimit,
        h.PreviousMaxMonthlyBookings,
        h.NewMaxMonthlyBookings,
        h.PreviousBillingCycle,
        h.NewBillingCycle,
        h.PreviousContractAmount,
        h.NewContractAmount,
        h.EffectiveDate,
        h.ExpiryDate,
        h.Notes,
        h.ChangedByName,
        h.CreatedAt
    };
}
