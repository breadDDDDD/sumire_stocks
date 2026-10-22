using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Relations;

/// <summary>
/// Latest statistical links, kept in memory and mirrored to %LOCALAPPDATA%/Sumire/cache/relations-stats.json
/// so they survive restarts without a DB table.
/// </summary>
public sealed class RelationsStatsStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly string _path;
    readonly ILogger<RelationsStatsStore> _logger;
    StatisticalSnapshot? _snapshot;

    public RelationsStatsStore(ILogger<RelationsStatsStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sumire", "cache", "relations-stats.json");
    }

    public StatisticalSnapshot Snapshot => _snapshot ??= LoadFile();

    public bool HasData => Snapshot.AsOf != default;

    public void Save(StatisticalSnapshot snapshot)
    {
        _snapshot = snapshot;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not write {Path}: {Error}", _path, ex.Message);
        }
    }

    StatisticalSnapshot LoadFile()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<StatisticalSnapshot>(File.ReadAllText(_path), Json) ?? StatisticalSnapshot.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Ignoring unreadable {Path}: {Error}", _path, ex.Message);
        }
        return StatisticalSnapshot.Empty;
    }
}

/// <summary>EOD step: recomputes correlation clusters and lead-lag links from daily bars in the DB.</summary>
public sealed class RelationsStatsStep(SumireDbContext db, InstrumentCatalog catalog, RelationsStatsStore store, TimeProvider clock,
    ILogger<RelationsStatsStep> logger) : IEodPipelineStep
{
    // Enough calendar days for LeadLagWindow (120) trading days plus holidays.
    const int LookbackDays = 220;

    public int Order => 50;
    public string Name => "relations-stats";

    public async Task RunAsync(DateOnly tradingDate, CancellationToken ct)
    {
        var stocks = catalog.Core.Where(i => !i.IsIndex).Select(i => i.Code).ToHashSet();
        var proxies = StatisticalLinks.ProxyCodes.Where(c => catalog.Find(c) is not null).ToHashSet();
        var codes = stocks.Concat(proxies).ToList();
        var from = tradingDate.AddDays(-LookbackDays);

        var rows = await db.DailyBars.AsNoTracking()
            .Where(b => codes.Contains(b.Code) && b.Date >= from && b.Date <= tradingDate)
            .OrderBy(b => b.Code).ThenBy(b => b.Date)
            .ToListAsync(ct);
        var bars = rows.GroupBy(b => b.Code).ToDictionary(g => g.Key,
            g => (IReadOnlyList<Bar>)g.Select(b => new Bar(b.Date, b.Open, b.High, b.Low, b.Close, b.AdjClose, b.Volume)).ToList());

        var stockBars = bars.Where(kv => stocks.Contains(kv.Key)).ToDictionary();
        var proxyBars = bars.Where(kv => proxies.Contains(kv.Key)).ToDictionary();
        var snapshot = new StatisticalSnapshot(tradingDate, Wib.Now(clock),
            StatisticalLinks.CorrelationClusters(stockBars),
            StatisticalLinks.LeadLags(stockBars, proxyBars));
        store.Save(snapshot);
        logger.LogInformation("Relations stats for {Date}: {Stocks} stocks, {Corr} with correlated peers, {LeadLag} with lead-lag links",
            tradingDate, stockBars.Count, snapshot.Correlated.Count, snapshot.LeadLag.Count);
    }
}
