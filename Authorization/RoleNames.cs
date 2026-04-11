namespace Beauty.Api.Authorization;

/// <summary>Canonical role name constants — matches EnterpriseRole.Name seeds.</summary>
public static class RoleNames
{
    // Platform (Saqqara internal)
    public const string SuperAdmin      = "SuperAdmin";
    public const string PlatformAdmin   = "PlatformAdmin";
    public const string PlatformSupport = "PlatformSupport";

    // Enterprise (tenant-scoped)
    public const string EnterpriseOwner   = "EnterpriseOwner";
    public const string EnterpriseManager = "EnterpriseManager";
    public const string Artist            = "Artist";
    public const string Receptionist      = "Receptionist";

    public static readonly string[] PlatformRoles =
        [SuperAdmin, PlatformAdmin, PlatformSupport];

    public static readonly string[] EnterpriseRoles =
        [EnterpriseOwner, EnterpriseManager, Artist, Receptionist];
}
