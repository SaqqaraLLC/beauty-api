namespace Beauty.Api.Models.Subscriptions;

public enum SubscriptionStatus
{
    Trialing  = 0,
    Active    = 1,
    PastDue   = 2,
    Suspended = 3,
    Cancelled = 4
}
