using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Services;

public class BattleAutoResolveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BattleAutoResolveService> _logger;

    public BattleAutoResolveService(IServiceScopeFactory scopeFactory, ILogger<BattleAutoResolveService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ResolveExpiredBattlesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BattleAutoResolve error");
            }
        }
    }

    private async Task ResolveExpiredBattlesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeautyDbContext>();

        var expired = await db.ArtistBattles
            .Where(b => b.Status == BattleStatus.Active && b.EndsAt <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var battle in expired)
        {
            battle.Status = BattleStatus.Completed;
            battle.WinnerUserId = battle.Artist1TotalSlabs >= battle.Artist2TotalSlabs
                ? battle.Artist1UserId
                : battle.Artist2UserId;

            _logger.LogInformation(
                "Battle {Id} auto-resolved. Winner: {Winner} ({A1} vs {A2} slabs)",
                battle.Id, battle.WinnerUserId,
                battle.Artist1TotalSlabs, battle.Artist2TotalSlabs);
        }

        if (expired.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
