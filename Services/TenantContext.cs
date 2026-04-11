using Beauty.Api.Authorization;
using System.Security.Claims;

namespace Beauty.Api.Services;

/// <summary>
/// Resolves the current tenant from the authenticated user's JWT/cookie claims.
/// Injected into DbContext so global query filters can enforce tenant isolation.
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? CurrentTenantId
    {
        get
        {
            var raw = _accessor.HttpContext?.User.FindFirstValue("tenant_id");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsPlatformUser
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user == null) return false;
            return user.IsInRole(RoleNames.SuperAdmin)
                || user.IsInRole(RoleNames.PlatformAdmin)
                || user.IsInRole(RoleNames.PlatformSupport);
        }
    }
}
