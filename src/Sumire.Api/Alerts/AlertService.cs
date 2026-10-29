using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Hubs;
using Sumire.Api.Market;

namespace Sumire.Api.Alerts;

/// <summary>A fired alert as pushed over SignalR ("alert") and listed by GET /api/alerts/recent.</summary>
public sealed record AlertEvent(long Id, string? Code, string Type, string Message, DateTimeOffset Time);

/// <summary>The last fired alerts in memory, so a tab that opens later still sees today's alerts.</summary>
public sealed class AlertFeed
{
    const int Max = 50;
    readonly ConcurrentQueue<AlertEvent> _events = new();

    public void Add(AlertEvent e)
    {
        _events.Enqueue(e);
        while (_events.Count > Max) _events.TryDequeue(out _);
    }

    public IReadOnlyList<AlertEvent> Recent => _events.Reverse().ToList();
}

/// <summary>Evaluates stored alerts and fires them (last_triggered_at + SignalR push).</summary>
public sealed class AlertService(
    SumireDbContext db,
    MarketDataService market,
    AlertFeed feed,
    IHubContext<QuotesHub> hub,
    TimeProvider clock,
    ILogger<AlertService> logger)
{
    public const string AlertEventName = "alert";
    static readonly TimeSpan InsightWindow = TimeSpan.FromDays(2);

    /// <summary>Price and % alerts against current quotes. Returns the number fired.</summary>
    public async Task<int> CheckQuoteAlertsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var alerts = (await db.Alerts.Where(a => a.Active && AlertRules.QuoteTypes.Contains(a.Type) && a.Code != null).ToListAsync(ct))
            .Where(a => AlertRules.CanFire(a, now)).ToList();
        if (alerts.Count == 0) return 0;

        // GetQuotesAsync serves at most MaxCodesPerConnection codes per call.
        var quotes = new Dictionary<string, Quote>();
        foreach (var chunk in alerts.Select(a => a.Code!).Distinct().Chunk(QuoteSubscriptions.MaxCodesPerConnection))
            foreach (var q in await market.GetQuotesAsync(chunk, ct))
                quotes[q.Code] = q;
        var fired = 0;
        foreach (var alert in alerts)
            if (quotes.TryGetValue(alert.Code!, out var q) && AlertRules.IsTodays(q, now) && AlertRules.EvaluateQuote(alert, q) is { } message)
                fired += await FireAsync(alert, message, now, ct);
        return fired;
    }

    /// <summary>Indicator alerts against daily bars (run after the EOD bars land).</summary>
    public async Task<int> CheckBarAlertsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var alerts = (await db.Alerts.Where(a => a.Active && AlertRules.BarTypes.Contains(a.Type) && a.Code != null).ToListAsync(ct))
            .Where(a => AlertRules.CanFire(a, now)).ToList();
        var fired = 0;
        var since = Wib.Today(clock).AddDays(-420);
        foreach (var group in alerts.GroupBy(a => a.Code!))
        {
            var bars = (await market.GetDailyBarsAsync(group.Key, since, ct))
                .Select(b => new Bar(b.Date, b.Open, b.High, b.Low, b.Close, b.AdjClose, b.Volume)).ToList();
            foreach (var alert in group)
                if (AlertRules.EvaluateBars(alert, bars) is { } message)
                    fired += await FireAsync(alert, message, now, ct);
        }
        return fired;
    }

    /// <summary>negative_insight alerts against insights created in the last two days and after the alert's last firing.</summary>
    public async Task<int> CheckInsightAlertsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var alerts = (await db.Alerts.Where(a => a.Active && a.Type == AlertRules.NegativeInsight).ToListAsync(ct))
            .Where(a => AlertRules.CanFire(a, now)).ToList();
        if (alerts.Count == 0) return 0;

        var since = now - InsightWindow;
        var insights = await db.Insights.AsNoTracking()
            .Where(i => i.Kind == "stock" && i.CreatedAt >= since)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        if (insights.Count == 0) return 0;
        var watchlistCodes = await db.WatchlistItems.Select(w => w.Code).Distinct().ToListAsync(ct);

        var fired = 0;
        foreach (var alert in alerts)
        {
            var codes = alert.Code is { } c ? [c] : watchlistCodes;
            var after = alert.LastTriggeredAt ?? alert.CreatedAt;
            var hit = insights.FirstOrDefault(i => codes.Contains(i.Subject) && i.CreatedAt > after && AlertRules.EvaluateInsight(alert, i) is not null);
            if (hit is not null) fired += await FireAsync(alert, AlertRules.EvaluateInsight(alert, hit)!, now, ct, hit.Subject);
        }
        return fired;
    }

    async Task<int> FireAsync(Alert alert, string message, DateTimeOffset now, CancellationToken ct, string? code = null)
    {
        alert.LastTriggeredAt = now;
        await db.SaveChangesAsync(ct);
        var e = new AlertEvent(alert.Id, code ?? alert.Code, alert.Type, message, Wib.ToWib(now));
        feed.Add(e);
        logger.LogInformation("{Message}", message);
        try { await hub.Clients.All.SendAsync(AlertEventName, e, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Alert push failed: {Error}", ex.Message); }
        return 1;
    }
}

/// <summary>Indicator alerts once the day's bars are stored.</summary>
public sealed class AlertEodStep(AlertService alerts) : IEodPipelineStep
{
    public int Order => 900;
    public string Name => "alerts";
    public Task RunAsync(DateOnly tradingDate, CancellationToken ct) => alerts.CheckBarAlertsAsync(ct);
}
