using System.Collections.Concurrent;

namespace Sumire.Api.Market;

/// <summary>
/// A step that runs after the EOD job has stored the day's bars (indicators, scores, forecasts…).
/// Register with <c>services.AddScoped&lt;IEodPipelineStep, MyStep&gt;()</c>; steps run in ascending <see cref="Order"/>.
/// A failing step is logged and the next one still runs.
/// </summary>
public interface IEodPipelineStep
{
    int Order { get; }
    string Name { get; }
    Task RunAsync(DateOnly tradingDate, CancellationToken ct);
}

/// <summary>Process-wide freshness info for /api/status.</summary>
public sealed class MarketStatus
{
    public DateTimeOffset? LastEodRun { get; set; }
    public string? LastEodResult { get; set; }
    public string? LastEodError { get; set; }
    public DateTimeOffset? NextEodRun { get; set; }
    public bool EodRunning { get; set; }
    public DateTimeOffset? LastQuoteTime { get; set; }
    public DateTimeOffset? LastPoll { get; set; }
}

/// <summary>Which codes each connected browser tab wants live quotes for.</summary>
public sealed class QuoteSubscriptions
{
    public const int MaxCodesPerConnection = 200;

    readonly ConcurrentDictionary<string, HashSet<string>> _byConnection = new();

    public int ConnectionCount => _byConnection.Count;

    public IReadOnlyCollection<string> Connections => _byConnection.Keys.ToList();

    public void Connect(string connectionId) => _byConnection.TryAdd(connectionId, []);

    public void Disconnect(string connectionId) => _byConnection.TryRemove(connectionId, out _);

    public void Subscribe(string connectionId, IEnumerable<string> codes)
    {
        var set = _byConnection.GetOrAdd(connectionId, _ => []);
        lock (set)
            foreach (var c in Normalize(codes))
                if (set.Count < MaxCodesPerConnection) set.Add(c);
    }

    public void Unsubscribe(string connectionId, IEnumerable<string> codes)
    {
        if (!_byConnection.TryGetValue(connectionId, out var set)) return;
        lock (set)
            foreach (var c in Normalize(codes)) set.Remove(c);
    }

    public IReadOnlyList<string> CodesFor(string connectionId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var set)) return [];
        lock (set) return set.ToList();
    }

    public IReadOnlySet<string> AllCodes()
    {
        var all = new HashSet<string>();
        foreach (var set in _byConnection.Values)
            lock (set) all.UnionWith(set);
        return all;
    }

    public static IEnumerable<string> Normalize(IEnumerable<string> codes) =>
        codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct();
}
