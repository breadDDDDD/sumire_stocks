using Microsoft.AspNetCore.SignalR;
using Sumire.Api.Hubs;

namespace Sumire.Api.Research;

public sealed record InsightReady(string Kind, string Subject, long InsightId);

public interface IInsightNotifier
{
    Task InsightReadyAsync(InsightReady message, CancellationToken ct);
}

/// <summary>
/// Pushes "insightReady" to every client of the quotes hub (/hubs/quotes). CLI verbs register SignalR without
/// a server, so there it reaches nobody and the UI picks the insight up on its next read.
/// </summary>
public sealed class SignalRInsightNotifier(IHubContext<QuotesHub> hub, ILogger<SignalRInsightNotifier> logger) : IInsightNotifier
{
    public const string Method = "insightReady";

    public async Task InsightReadyAsync(InsightReady message, CancellationToken ct)
    {
        try { await hub.Clients.All.SendAsync(Method, message, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not push {Method}", Method);
        }
    }
}
