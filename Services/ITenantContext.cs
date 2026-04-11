namespace Beauty.Api.Services;

public interface ITenantContext
{
    /// <summary>The EnterpriseAccountId of the authenticated user. Null for platform-level users.</summary>
    Guid? CurrentTenantId { get; }

    /// <summary>True for SuperAdmin / PlatformAdmin — bypasses tenant query filters.</summary>
    bool IsPlatformUser { get; }
}
