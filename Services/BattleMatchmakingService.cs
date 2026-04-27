using Beauty.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Services;

public class BattleMatchmakingService
{
    private readonly BeautyDbContext _db;

    public BattleMatchmakingService(BeautyDbContext db) => _db = db;

    /// <summary>
    /// Composite score: tenure (months on platform) + profit (gift slabs received) + popularity (bookings + ratings).
    /// Used to pair artists of similar standing in battles.
    /// </summary>
    public async Task<double> CalculateScoreAsync(string artistUserId)
    {
        var profile = await _db.ArtistProfiles
            .FirstOrDefaultAsync(p => p.UserId == artistUserId);

        double tenureScore = 0;
        double popularityScore = 0;

        if (profile != null)
        {
            var tenureMonths = (DateTime.UtcNow - profile.CreatedAt).TotalDays / 30.0;
            tenureScore      = Math.Min(tenureMonths * 2.0, 72);  // cap at 36 months = 72 pts

            // Popularity: bookings + weighted rating
            popularityScore = (profile.BookingCount * 3.0)
                            + (profile.AverageRating * profile.ReviewCount * 0.5);
        }

        // Profit: total gift slabs received (every 50 slabs = 1 point, cap 200 pts)
        var totalGiftSlabs = await _db.GiftTransactions
            .Where(g => g.RecipientArtistUserId == artistUserId)
            .SumAsync(g => (double?)g.SlabsSpent) ?? 0;

        double profitScore = Math.Min(totalGiftSlabs / 50.0, 200);

        return Math.Round(tenureScore + profitScore + popularityScore, 2);
    }

    /// <summary>
    /// Finds the waiting signup whose BattleScore is closest to the candidate's score.
    /// </summary>
    public async Task<Beauty.Api.Models.Gifts.BattleSignup?> FindBestMatchAsync(
        string candidateUserId, int durationMinutes, double candidateScore)
    {
        var waiting = await _db.BattleSignups
            .Where(s => s.ArtistUserId != candidateUserId
                     && s.Status == Beauty.Api.Models.Gifts.BattleSignupStatus.Waiting
                     && s.PreferredDurationMinutes == durationMinutes)
            .ToListAsync();

        return waiting
            .OrderBy(s => Math.Abs(s.BattleScore - candidateScore))
            .FirstOrDefault();
    }
}
