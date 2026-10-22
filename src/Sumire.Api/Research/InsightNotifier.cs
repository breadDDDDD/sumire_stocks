using Microsoft.AspNetCore.SignalR;

namespace Sumire.Api.Research;

public sealed record InsightReady(string Kind, string Subject, long InsightId);

public interface IInsightNotifier
{
    Task InsightReadyAsync(InsightReady message, CancellationToken ct);
}

/// <summary>
/// Pushes "insightReady" to every client of the quotes hub (/hubs/quotes). The hub class lives in the Market area,
/// so it is resolved by name: in CLI verbs (no SignalR) or before the hub exists this is a no-op.
/// </summary>
public sealed class SignalRInsightNotifier(IServiceProvider services, ILogger<SignalRInsightNotifier> logger) : IInsightNotifier
{
    public const string Method = "insightReady";
    const string HubTypeName = "Sumire.Api.Hubs.QuotesHub";

    readonly Lazy<IHubClients?> _clients = new(() =>
    {
        var hubType = typeof(SignalRInsightNotifier).Assembly.GetType(HubTypeName);
        if (hubType is null) return null;
        var context = services.GetService(typeof(IHubContext<>).MakeGenericType(hubType));
        return context?.GetType().GetProperty(nameof(IHubContext.Clients))?.GetValue(context) as IHubClients;
    });

    public async Task InsightReadyAsync(InsightReady message, CancellationToken ct)
    {
        if (_clients.Value is not { } clients) return;
        try { await clients.All.SendAsync(Method, message, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not push {Method}", Method);
        }
    }
}
