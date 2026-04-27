namespace Beauty.Api.Services;

public class PowerAutomateSettings
{
    // Webhook secret shared with Power Automate for callback auth
    public string WebhookSecret { get; set; } = "";

    // Push webhook URLs — Power Automate HTTP trigger endpoints
    public string ExpenseSubmittedUrl  { get; set; } = "";
    public string ModerationAlertUrl   { get; set; } = "";
    public string ArtistApprovedUrl    { get; set; } = "";

    // Polled by PA on schedule — no URL needed, PA calls our API directly
    // MonthlyCompensation: PA → GET /api/reports/monthly-compensation
    // SubscriptionRenewals: PA → GET /api/subscriptions/upcoming-renewals?days=3
    // EnterpriseRenewals: PA → GET /api/enterprise/renewals-due?days=30
}
