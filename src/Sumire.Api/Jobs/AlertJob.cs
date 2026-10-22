using Sumire.Api.Alerts;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Jobs;

/// <summary>
/// Every 60 s while a browser tab is connected: price/% alerts during IDX sessions and negative-insight alerts
/// any time. Alerts are shown in the UI, so with no tab open there is nobody to tell and nothing runs.
/// Indicator alerts run in the EOD pipeline (<see cref="AlertEodStep"/>).
/// </summary>
public sealed class AlertJob(
    IServiceScopeFactory scopes,
    QuoteSubscriptions subscriptions,
    MarketCalendar calendar,
    TimeProvider clock,
    ILogger<AlertJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public static (bool Quotes, bool Insights) WhatToCheck(MarketSession session, int connections, bool dbConfigured) =>
        !dbConfigured || connections == 0 ? (false, false) : (MarketCalendar.IsOpen(session), true);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(ct))
        {
            var (quotes, insights) = WhatToCheck(calendar.CurrentSession, subscriptions.ConnectionCount, Db.IsConfigured);
            if (!quotes && !insights) continue;
            try
            {
                using var scope = scopes.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<AlertService>();
                if (quotes) await alerts.CheckQuoteAlertsAsync(ct);
                if (insights) await alerts.CheckInsightAlertsAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Alert check failed: {Error}", Db.ShortError(ex));
            }
        }
    }
}
