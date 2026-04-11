namespace Beauty.Api.Authorization;

using P = Permissions;

/// <summary>
/// Role-permission matrix for the Saqqara multi-tenant platform.
///
/// Platform roles  — Scope = "Platform" — Saqqara internal staff.
/// Enterprise roles — Scope = "Enterprise" — belong to a tenant/EnterpriseAccount.
///
/// Matrix:
/// ┌──────────────────────┬──────┬──────┬──────┬─────┬──────┬────────┬─────────────┐
/// │ Permission           │ SAdm │ PAdm │ PSup │ EOw │ EMgr │ Artist │ Receptionist│
/// ├──────────────────────┼──────┼──────┼──────┼─────┼──────┼────────┼─────────────┤
/// │ tenant.read          │  ✓   │  ✓   │  ✓   │  ✓  │      │        │             │
/// │ tenant.update        │  ✓   │  ✓   │      │  ✓  │      │        │             │
/// │ tenant.suspend       │  ✓   │  ✓   │      │     │      │        │             │
/// │ location.create      │  ✓   │  ✓   │      │  ✓  │  ✓   │        │             │
/// │ location.read        │  ✓   │  ✓   │  ✓   │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ location.update      │  ✓   │  ✓   │      │  ✓  │  ✓   │        │             │
/// │ location.delete      │  ✓   │  ✓   │      │  ✓  │      │        │             │
/// │ user.invite          │  ✓   │  ✓   │      │  ✓  │  ✓   │        │             │
/// │ user.read            │  ✓   │  ✓   │  ✓   │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ user.update          │  ✓   │  ✓   │      │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ user.suspend         │  ✓   │  ✓   │      │  ✓  │      │        │             │
/// │ user.offboard        │  ✓   │  ✓   │      │  ✓  │      │        │             │
/// │ client.create        │  ✓   │      │      │  ✓  │  ✓   │        │      ✓      │
/// │ client.read          │  ✓   │      │      │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ client.update        │  ✓   │      │      │  ✓  │  ✓   │        │      ✓      │
/// │ booking.create       │  ✓   │      │      │  ✓  │  ✓   │        │      ✓      │
/// │ booking.read         │  ✓   │      │      │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ booking.update       │  ✓   │      │      │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ booking.cancel       │  ✓   │      │      │  ✓  │  ✓   │   ✓    │      ✓      │
/// │ booking.approve      │  ✓   │      │      │  ✓  │  ✓   │   ✓    │             │
/// │ payment.read         │  ✓   │  ✓   │      │  ✓  │  ✓   │        │             │
/// │ payment.process      │  ✓   │      │      │  ✓  │  ✓   │        │             │
/// │ payment.refund       │  ✓   │  ✓   │      │  ✓  │      │        │             │
/// │ report.read          │  ✓   │  ✓   │  ✓   │  ✓  │  ✓   │        │             │
/// │ auditlog.read        │  ✓   │  ✓   │  ✓   │  ✓  │      │        │             │
/// └──────────────────────┴──────┴──────┴──────┴─────┴──────┴────────┴─────────────┘
/// </summary>
public static class PermissionMatrix
{
    // ── Platform roles ─────────────────────────────────────────────────────────

    public static readonly string[] SuperAdmin = P.All; // every permission

    public static readonly string[] PlatformAdmin =
    [
        P.Tenant.Read,    P.Tenant.Update,   P.Tenant.Suspend,
        P.Locations.Read, P.Locations.Create, P.Locations.Update, P.Locations.Delete,
        P.Users.Invite,   P.Users.Read,      P.Users.Update,     P.Users.Suspend, P.Users.Offboard,
        P.Payments.Read,  P.Payments.Refund,
        P.Reports.Read,   P.Audit.Read,
    ];

    public static readonly string[] PlatformSupport =
    [
        P.Tenant.Read,
        P.Locations.Read,
        P.Users.Read,
        P.Reports.Read,
        P.Audit.Read,
    ];

    // ── Enterprise roles ───────────────────────────────────────────────────────

    public static readonly string[] EnterpriseOwner =
    [
        P.Tenant.Read,    P.Tenant.Update,
        P.Locations.Create, P.Locations.Read, P.Locations.Update, P.Locations.Delete,
        P.Users.Invite,   P.Users.Read,   P.Users.Update,   P.Users.Suspend, P.Users.Offboard,
        P.Clients.Create, P.Clients.Read, P.Clients.Update,
        P.Bookings.Create, P.Bookings.Read, P.Bookings.Update, P.Bookings.Cancel, P.Bookings.Approve,
        P.Payments.Read,  P.Payments.Process, P.Payments.Refund,
        P.Reports.Read,   P.Audit.Read,
    ];

    public static readonly string[] EnterpriseManager =
    [
        P.Locations.Create, P.Locations.Read, P.Locations.Update,
        P.Users.Invite,     P.Users.Read,     P.Users.Update,
        P.Clients.Create,   P.Clients.Read,   P.Clients.Update,
        P.Bookings.Create,  P.Bookings.Read,  P.Bookings.Update, P.Bookings.Cancel, P.Bookings.Approve,
        P.Payments.Read,    P.Payments.Process,
        P.Reports.Read,
    ];

    public static readonly string[] Artist =
    [
        P.Locations.Read,
        P.Users.Read,     P.Users.Update,
        P.Clients.Read,
        P.Bookings.Read,  P.Bookings.Update, P.Bookings.Cancel, P.Bookings.Approve,
    ];

    public static readonly string[] Receptionist =
    [
        P.Locations.Read,
        P.Users.Read,     P.Users.Update,
        P.Clients.Create, P.Clients.Read,   P.Clients.Update,
        P.Bookings.Create, P.Bookings.Read, P.Bookings.Update, P.Bookings.Cancel,
    ];

    /// <summary>
    /// Maps role name → permission set.
    /// Used by the seeder and the auth middleware.
    /// </summary>
    public static readonly Dictionary<string, string[]> ByRole = new()
    {
        [RoleNames.SuperAdmin]        = SuperAdmin,
        [RoleNames.PlatformAdmin]     = PlatformAdmin,
        [RoleNames.PlatformSupport]   = PlatformSupport,
        [RoleNames.EnterpriseOwner]   = EnterpriseOwner,
        [RoleNames.EnterpriseManager] = EnterpriseManager,
        [RoleNames.Artist]            = Artist,
        [RoleNames.Receptionist]      = Receptionist,
    };
}
