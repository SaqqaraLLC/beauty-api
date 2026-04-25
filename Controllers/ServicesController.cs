using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/services")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly BeautyDbContext _db;

    public ServicesController(BeautyDbContext db) => _db = db;

    // ── GET /api/services ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? category,
        [FromQuery] bool?   activeOnly)
    {
        var query = _db.Services
            .AsNoTracking()
            .Include(s => s.Category)
            .Include(s => s.AddOns.Where(a => a.IsActive).OrderBy(a => a.SortOrder))
            .AsQueryable();

        if (activeOnly != false)
            query = query.Where(s => s.Active);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category != null && s.Category.Key == category);

        var services = await query.OrderBy(s => s.Name).ToListAsync();

        return Ok(services.Select(s => MapService(s)));
    }

    // ── GET /api/services/{id} ─────────────────────────────────────────────────

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var service = await _db.Services
            .AsNoTracking()
            .Include(s => s.Category)
            .Include(s => s.AddOns.Where(a => a.IsActive).OrderBy(a => a.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);

        if (service is null) return NotFound();
        return Ok(MapService(service));
    }

    // ── POST /api/services ─────────────────────────────────────────────────────

    public record CreateServiceReq(
        string   Name,
        string?  Description,
        decimal  Price,
        int      DurationMinutes,
        long?    CategoryId);

    [HttpPost]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Create([FromBody] CreateServiceReq req)
    {
        var service = new Service
        {
            Name            = req.Name,
            Description     = req.Description,
            Price           = req.Price,
            DurationMinutes = req.DurationMinutes,
            CategoryId      = req.CategoryId,
            Active          = true
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = service.Id }, MapService(service));
    }

    // ── PUT /api/services/{id} ─────────────────────────────────────────────────

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Update(long id, [FromBody] CreateServiceReq req)
    {
        var service = await _db.Services.FindAsync(id);
        if (service is null) return NotFound();

        service.Name            = req.Name;
        service.Description     = req.Description;
        service.Price           = req.Price;
        service.DurationMinutes = req.DurationMinutes;
        service.CategoryId      = req.CategoryId;

        await _db.SaveChangesAsync();
        return Ok(MapService(service));
    }

    // ── DELETE /api/services/{id} ──────────────────────────────────────────────

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(long id)
    {
        var service = await _db.Services.FindAsync(id);
        if (service is null) return NotFound();

        service.Active = false;
        await _db.SaveChangesAsync();
        return Ok(new { id, active = false });
    }

    // ── Add-Ons ────────────────────────────────────────────────────────────────

    public record AddOnReq(
        string   Name,
        string?  Description,
        decimal  Price,
        int      ExtraMinutes,
        int      SortOrder);

    [HttpGet("{id:long}/addons")]
    public async Task<IActionResult> GetAddOns(long id)
    {
        var addons = await _db.ServiceAddOns
            .AsNoTracking()
            .Where(a => a.ServiceId == id && a.IsActive)
            .OrderBy(a => a.SortOrder)
            .ToListAsync();

        return Ok(addons.Select(MapAddOn));
    }

    [HttpPost("{id:long}/addons")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> AddAddOn(long id, [FromBody] AddOnReq req)
    {
        if (!await _db.Services.AnyAsync(s => s.Id == id)) return NotFound();

        var addOn = new ServiceAddOn
        {
            ServiceId    = id,
            Name         = req.Name,
            Description  = req.Description,
            Price        = req.Price,
            ExtraMinutes = req.ExtraMinutes,
            SortOrder    = req.SortOrder,
            IsActive     = true
        };

        _db.ServiceAddOns.Add(addOn);
        await _db.SaveChangesAsync();
        return Ok(MapAddOn(addOn));
    }

    [HttpPut("{id:long}/addons/{addOnId:long}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> UpdateAddOn(long id, long addOnId, [FromBody] AddOnReq req)
    {
        var addOn = await _db.ServiceAddOns.FirstOrDefaultAsync(a => a.Id == addOnId && a.ServiceId == id);
        if (addOn is null) return NotFound();

        addOn.Name         = req.Name;
        addOn.Description  = req.Description;
        addOn.Price        = req.Price;
        addOn.ExtraMinutes = req.ExtraMinutes;
        addOn.SortOrder    = req.SortOrder;

        await _db.SaveChangesAsync();
        return Ok(MapAddOn(addOn));
    }

    [HttpDelete("{id:long}/addons/{addOnId:long}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> DeleteAddOn(long id, long addOnId)
    {
        var addOn = await _db.ServiceAddOns.FirstOrDefaultAsync(a => a.Id == addOnId && a.ServiceId == id);
        if (addOn is null) return NotFound();

        addOn.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { addOnId, isActive = false });
    }

    // ── Categories ─────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCategories()
    {
        var cats = await _db.ServiceCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayName)
            .Select(c => new { c.Id, c.Key, c.DisplayName })
            .ToListAsync();

        return Ok(cats);
    }

    public record CategoryReq(string Key, string DisplayName);

    [HttpPost("categories")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryReq req)
    {
        if (await _db.ServiceCategories.AnyAsync(c => c.Key == req.Key))
            return Conflict(new { error = $"Category key '{req.Key}' already exists" });

        var cat = new ServiceCategory { Key = req.Key, DisplayName = req.DisplayName, IsActive = true };
        _db.ServiceCategories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { cat.Id, cat.Key, cat.DisplayName });
    }

    [HttpPut("categories/{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateCategory(long id, [FromBody] CategoryReq req)
    {
        var cat = await _db.ServiceCategories.FindAsync(id);
        if (cat is null) return NotFound();

        cat.Key         = req.Key;
        cat.DisplayName = req.DisplayName;
        await _db.SaveChangesAsync();
        return Ok(new { cat.Id, cat.Key, cat.DisplayName });
    }

    [HttpDelete("categories/{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCategory(long id)
    {
        var cat = await _db.ServiceCategories.FindAsync(id);
        if (cat is null) return NotFound();

        cat.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { id, isActive = false });
    }

    // ── Mappers ────────────────────────────────────────────────────────────────

    private static object MapService(Service s) => new
    {
        s.Id,
        s.Name,
        s.Description,
        s.Price,
        s.DurationMinutes,
        s.Active,
        Category = s.Category is null ? null : new { s.Category.Id, s.Category.Key, s.Category.DisplayName },
        AddOns   = s.AddOns.Select(MapAddOn)
    };

    private static object MapAddOn(ServiceAddOn a) => new
    {
        a.Id,
        a.ServiceId,
        a.Name,
        a.Description,
        a.Price,
        a.ExtraMinutes,
        a.SortOrder,
        a.IsActive
    };
}
