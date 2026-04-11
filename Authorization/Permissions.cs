namespace Beauty.Api.Authorization;

/// <summary>
/// Atomic permission strings used as ASP.NET Core policy names and JWT claims.
/// Format: "domain.action"
/// </summary>
public static class Permissions
{
    public static class Tenant
    {
        public const string Read    = "tenant.read";
        public const string Update  = "tenant.update";
        public const string Suspend = "tenant.suspend";
    }

    public static class Locations
    {
        public const string Create = "location.create";
        public const string Read   = "location.read";
        public const string Update = "location.update";
        public const string Delete = "location.delete";
    }

    public static class Users
    {
        public const string Invite   = "user.invite";
        public const string Read     = "user.read";
        public const string Update   = "user.update";
        public const string Suspend  = "user.suspend";
        public const string Offboard = "user.offboard";
    }

    public static class Clients
    {
        public const string Create = "client.create";
        public const string Read   = "client.read";
        public const string Update = "client.update";
    }

    public static class Bookings
    {
        public const string Create  = "booking.create";
        public const string Read    = "booking.read";
        public const string Update  = "booking.update";
        public const string Cancel  = "booking.cancel";
        public const string Approve = "booking.approve";
    }

    public static class Payments
    {
        public const string Read    = "payment.read";
        public const string Process = "payment.process";
        public const string Refund  = "payment.refund";
    }

    public static class Reports
    {
        public const string Read = "report.read";
    }

    public static class Audit
    {
        public const string Read = "auditlog.read";
    }

    /// <summary>All permission strings — used to register policies in Program.cs</summary>
    public static readonly string[] All =
    [
        Tenant.Read,    Tenant.Update,    Tenant.Suspend,
        Locations.Create, Locations.Read, Locations.Update, Locations.Delete,
        Users.Invite,   Users.Read,       Users.Update,     Users.Suspend, Users.Offboard,
        Clients.Create, Clients.Read,     Clients.Update,
        Bookings.Create, Bookings.Read,   Bookings.Update,  Bookings.Cancel, Bookings.Approve,
        Payments.Read,  Payments.Process, Payments.Refund,
        Reports.Read,
        Audit.Read,
    ];
}
