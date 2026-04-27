namespace Beauty.Api.Models.Enterprise;

/// <summary>
/// Static tier definitions — not stored in DB; referenced by EnterpriseAccount.PlanTier.
/// </summary>
public static class EnterpriseTier
{
    public const string Starter    = "Starter";
    public const string Growth     = "Growth";
    public const string Enterprise = "Enterprise";
    public const string Custom     = "Custom";

    public static readonly TierDefinition[] All =
    [
        new(Starter,    SeatLimit: 5,  MaxMonthlyBookings: 100,  MonthlyPrice: 199m,  YearlyPrice: 1_990m),
        new(Growth,     SeatLimit: 20, MaxMonthlyBookings: 500,  MonthlyPrice: 499m,  YearlyPrice: 4_990m),
        new(Enterprise, SeatLimit: 50, MaxMonthlyBookings: 2000, MonthlyPrice: 999m,  YearlyPrice: 9_990m),
        new(Custom,     SeatLimit: 0,  MaxMonthlyBookings: 0,    MonthlyPrice: 0m,    YearlyPrice: 0m),
    ];

    public static TierDefinition? Get(string tier) =>
        Array.Find(All, t => t.Name.Equals(tier, StringComparison.OrdinalIgnoreCase));
}

/// <param name="SeatLimit">0 = unlimited</param>
/// <param name="MaxMonthlyBookings">0 = unlimited</param>
public record TierDefinition(
    string  Name,
    int     SeatLimit,
    int     MaxMonthlyBookings,
    decimal MonthlyPrice,
    decimal YearlyPrice);
