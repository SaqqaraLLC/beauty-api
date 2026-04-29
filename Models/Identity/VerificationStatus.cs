namespace Beauty.Api.Models;

public enum VerificationStatus
{
    Unverified = 0,
    IdPending  = 1,   // Stripe Identity session created, awaiting result
    IdVerified = 2,   // ID confirmed by Stripe, address doc not yet uploaded
    AddressPending = 3, // Address doc uploaded, awaiting admin review
    Verified   = 4,   // Fully verified — can gift, book, enter streams
    Rejected   = 5,   // Verification failed or rejected by admin
}
