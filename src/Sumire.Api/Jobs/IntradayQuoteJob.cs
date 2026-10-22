using Microsoft.AspNetCore.SignalR;
using Sumire.Api.Hubs;
using Sumire.Api.Market;

namespace Sumire.Api.Jobs;

/// <summary>
/// Polls delayed Yahoo quotes every 60 s, but only while the IDX session is open AND a browser tab is
/// connected. Covers the codes tabs subscribed to plus the tape (indexes and macro). Idle otherwise.
/// </summary>
public sealed class IntradayQuoteJob(
    IServiceScopeFactory scopes,
    QuoteSubscriptions subscriptions,
    InstrumentCatalog catalog,
    MarketCalendar calendar,
    MarketStatus status,
    IHubContext<QuotesHub> hub,
    TimeProvider clock,
    ILogger<IntradayQuoteJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public static bool ShouldPoll(MarketSession session, int connections) => connections > 0 && MarketCalendar.IsOpen(session);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Quote poll failed: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>Returns the number of quotes pushed (0 when gated off).</summary>
    public async Task<int> PollOnceAsync(CancellationToken ct)
    {
        if (!ShouldPoll(calendar.CurrentSession, subscriptions.ConnectionCount)) return 0;

        var codes = subscriptions.AllCodes().Union(catalog.Tape.Select(i => i.Code)).ToList();
        using var scope = scopes.CreateScope();
        var market = scope.ServiceProvider.GetRequiredService<MarketDataService>();
        var quotes = (await market.RefreshLiveQuotesAsync(codes, ct)).ToDictionary(q => q.Code);
        status.LastPoll = clock.GetUtcNow();

        var tape = catalog.Tape.Select(i => i.Code).ToHashSet();
        var pushed = 0;
        foreach (var connection in subscriptions.Connections)
        {
            var mine = subscriptions.CodesFor(connection).Union(tape).Where(quotes.ContainsKey).Select(c => quotes[c]).ToList();
            if (mine.Count == 0) continue;
            await hub.Clients.Client(connection).SendAsync(QuotesHub.QuotesEvent, mine, ct);
            pushed += mine.Count;
        }
        return pushed;
    }
}
