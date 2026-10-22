using Microsoft.AspNetCore.SignalR;
using Sumire.Api.Market;

namespace Sumire.Api.Hubs;

/// <summary>
/// /hubs/quotes. Clients call Subscribe(codes) / Unsubscribe(codes) and receive "quotes" (Quote[]) events.
/// Other areas push their own events (e.g. "insight") through IHubContext&lt;QuotesHub&gt;.
/// </summary>
public sealed class QuotesHub(QuoteSubscriptions subscriptions, MarketDataService market, ILogger<QuotesHub> logger) : Hub
{
    public const string Path = "/hubs/quotes";
    public const string QuotesEvent = "quotes";

    public override Task OnConnectedAsync()
    {
        subscriptions.Connect(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Adds codes to this connection and immediately sends their current (cached or last close) quotes.</summary>
    public async Task Subscribe(string[] codes)
    {
        subscriptions.Subscribe(Context.ConnectionId, codes);
        try
        {
            var quotes = await market.GetQuotesAsync(codes, Context.ConnectionAborted);
            if (quotes.Count > 0) await Clients.Caller.SendAsync(QuotesEvent, quotes, Context.ConnectionAborted);
        }
        catch (Exception ex) when (!Context.ConnectionAborted.IsCancellationRequested)
        {
            logger.LogWarning("Initial quotes failed: {Error}", ex.Message);
        }
    }

    public Task Unsubscribe(string[] codes)
    {
        subscriptions.Unsubscribe(Context.ConnectionId, codes);
        return Task.CompletedTask;
    }
}
