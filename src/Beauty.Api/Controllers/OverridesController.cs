using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/overrides")]
[Authorize]
public class OverridesController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly EmailTemplateService _email;
    private readonly IConfiguration _cfg;

    public OverridesController(
        BeautyDbContext db,
        EmailTemplateService email,
        IConfiguration cfg)
    {
        _db = db;
        _email = email;
        _cfg = cfg;
    }

    public record OverrideReq(
        long ArtistId,
        long RequestedByOwnerId,
        DateTime ValidFrom,
        DateTime ValidTo,
        string? Reason
    );

    [HttpPost("request")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> RequestOverride([FromBody] OverrideReq dto)
    {
        var ov = new StreamQuotaOverride
        {
            ArtistId = dto.ArtistId,
            RequestedByOwnerId = dto.RequestedByOwnerId,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            Reason = dto.Reason,
            ApprovalStatus = "pending"
        };

        _db.StreamQuotaOverrides.Add(ov);
        await _db.SaveChangesAsync();

        var adminTo = _cfg["Email:AdminAlertsTo"] ?? "admin@saqqarallc.com";

        await _email.SendAdminAsync(
            adminTo,
            "Override request submitted",
            $"ArtistId={dto.ArtistId}, OwnerId={dto.RequestedByOwnerId}, Window={dto.ValidFrom:u}–{dto.ValidTo:u}, Reason={dto.Reason ?? "(none)"}"
        );

        return Created($"/api/overrides/{ov.OverrideId}", ov);
    }
}
