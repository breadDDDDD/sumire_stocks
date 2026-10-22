using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Jobs;

/// <summary>
/// Keeps <c>fundamentals</c> fresh: once a day it refreshes core stocks whose row is missing or older than
/// 7 days (so each stock is fetched about weekly). Stocks opened on demand refresh themselves when stale.
/// </summary>
public sealed class FundamentalsJob(IServiceScopeFactory scopes, InstrumentCatalog catalog, TimeProvider clock, ILogger<FundamentalsJob> logger)
    : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Db.IsConfigured) return;
        await Task.Delay(StartDelay, clock, ct);
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try { await RefreshStaleAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Fundamentals refresh failed: {Error}", Db.ShortError(ex));
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<int> RefreshStaleAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<FundamentalsService>();

        var cutoff = clock.GetUtcNow() - FundamentalsService.MaxAge;
        var fresh = (await db.Fundamentals.AsNoTracking().Where(f => f.UpdatedAt >= cutoff).Select(f => f.Code).ToListAsync(ct)).ToHashSet();
        var stale = catalog.Core.Where(i => !i.IsIndex && !fresh.Contains(i.Code)).Select(i => i.Code).Order().ToList();

        int ok = 0, failed = 0;
        foreach (var code in stale)
        {
            try
            {
                if (await service.RefreshAsync(code, save: true, ct) is not null) ok++;
            }
            catch (Exception ex) when (ex is HttpRequestException or YahooException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                failed++;
                logger.LogDebug("Fundamentals for {Code} failed: {Error}", code, ex.Message);
            }
        }
        if (stale.Count > 0) logger.LogInformation("Fundamentals refreshed: {Ok} ok, {Failed} failed", ok, failed);
        return ok;
    }
}
