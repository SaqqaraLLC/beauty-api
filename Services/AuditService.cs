using Beauty.Api.Data;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Http;

namespace Beauty.Api.Services;

public class AuditService
{
    private readonly BeautyDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(BeautyDbContext db, IHttpContextAccessor http)
    {
        _db  = db;
        _http = http;
    }

    /// <summary>
    /// Write an audit entry. Fire-and-forget safe — errors are swallowed so
    /// a logging failure never breaks the primary request.
    /// </summary>
    public async Task LogAsync(
        string actorUserId,
        string action,
        string?  targetEntity   = null,
        string?  details        = null,
        string?  actorEmail     = null,
        int?     resultCode     = null,
        Guid?    tenantId       = null)
    {
        try
        {
            var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId         = actorUserId,
                ActorEmail          = actorEmail,
                Action              = action,
                TargetEntity        = targetEntity,
                Details             = details,
                IpAddress           = ip,
                ResultCode          = resultCode,
                EnterpriseAccountId = tenantId,
                Timestamp           = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Audit failure must never crash the request
        }
    }
}
