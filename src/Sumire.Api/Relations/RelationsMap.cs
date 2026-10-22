using System.Text.Json;
using Sumire.Api.Common;

namespace Sumire.Api.Relations;

public sealed record RelationGroup(string Name, string[] Tickers, string? Note = null, string? AsOf = null);

public sealed record CommodityLink(string Factor, string? ProxySymbol, string[] Tickers, string Direction, string? Note = null);

public sealed record MacroLink(string Factor, string Direction, string[]? Tickers = null, string[]? Sectors = null, string? Note = null);

public sealed record SupplyChainStep(string Stage, string[] Tickers);

public sealed record SupplyChain(string Name, SupplyChainStep[] Steps);

public sealed record RelationsFile(
    string? Updated,
    RelationGroup[]? Groups,
    CommodityLink[]? Commodities,
    MacroLink[]? Macro,
    SupplyChain[]? SupplyChains);

/// <summary>A curated factor link for one ticker; Kind is "commodity" or "macro".</summary>
public sealed record FactorLink(string Factor, string Kind, string Direction, string? ProxyCode, string? Note);

/// <summary>Hand-curated relations from Seed/relations.json with lookups in both directions.</summary>
public sealed class RelationsMap
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Macro factors that have a market series in the instrument catalog.</summary>
    static readonly Dictionary<string, string> MacroProxies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USDIDR"] = "USDIDR",
        ["US10Y"] = "US10Y",
        ["CHINA"] = "SSE",
    };

    public string? Updated { get; }
    public IReadOnlyList<RelationGroup> Groups { get; }
    public IReadOnlyList<CommodityLink> Commodities { get; }
    public IReadOnlyList<MacroLink> Macro { get; }
    public IReadOnlyList<SupplyChain> SupplyChains { get; }

    public static readonly RelationsMap Empty = new(new RelationsFile(null, [], [], [], []));

    public RelationsMap(RelationsFile file)
    {
        Updated = file.Updated;
        Groups = (file.Groups ?? []).Select(g => g with { Tickers = Norm(g.Tickers) }).ToList();
        Commodities = (file.Commodities ?? []).Select(c => c with { Factor = c.Factor.ToUpperInvariant(), Tickers = Norm(c.Tickers) }).ToList();
        Macro = (file.Macro ?? []).Select(m => m with { Factor = m.Factor.ToUpperInvariant(), Tickers = Norm(m.Tickers) }).ToList();
        SupplyChains = (file.SupplyChains ?? [])
            .Select(s => s with { Steps = (s.Steps ?? []).Select(st => st with { Tickers = Norm(st.Tickers) }).ToArray() }).ToList();
    }

    public static RelationsMap Parse(string json) =>
        new(JsonSerializer.Deserialize<RelationsFile>(json, Json) ?? throw new InvalidDataException("relations.json is empty"));

    public static RelationsMap Load(string path) => Parse(File.ReadAllText(path));

    public IEnumerable<RelationGroup> GroupsFor(string code)
    {
        code = Code(code);
        return Groups.Where(g => g.Tickers.Contains(code));
    }

    /// <summary>Commodity and macro links of a ticker. Sector-based macro links match on <paramref name="sector"/>.</summary>
    public IEnumerable<FactorLink> FactorsFor(string code, string? sector = null)
    {
        code = Code(code);
        foreach (var c in Commodities.Where(c => c.Tickers.Contains(code)))
            yield return new FactorLink(c.Factor, "commodity", c.Direction, c.ProxySymbol, c.Note);
        foreach (var m in Macro.Where(m => m.Tickers!.Contains(code) || sector is not null && (m.Sectors?.Contains(sector, StringComparer.OrdinalIgnoreCase) ?? false)))
            yield return new FactorLink(m.Factor, "macro", m.Direction, MacroProxies.GetValueOrDefault(m.Factor), m.Note);
    }

    /// <summary>All entries for a factor (a factor can have a positive and a negative entry). Empty when unknown.</summary>
    public IReadOnlyList<(FactorLink Link, string[] Tickers, string[] Sectors)> Factor(string factor)
    {
        factor = factor.Trim().ToUpperInvariant();
        var list = new List<(FactorLink, string[], string[])>();
        foreach (var c in Commodities.Where(c => c.Factor == factor))
            list.Add((new FactorLink(c.Factor, "commodity", c.Direction, c.ProxySymbol, c.Note), c.Tickers, []));
        foreach (var m in Macro.Where(m => m.Factor == factor))
            list.Add((new FactorLink(m.Factor, "macro", m.Direction, MacroProxies.GetValueOrDefault(m.Factor), m.Note), m.Tickers!, m.Sectors ?? []));
        return list;
    }

    public IEnumerable<string> FactorNames =>
        Commodities.Select(c => c.Factor).Concat(Macro.Select(m => m.Factor)).Distinct();

    public IEnumerable<SupplyChain> SupplyChainsFor(string code)
    {
        code = Code(code);
        return SupplyChains.Where(s => s.Steps.Any(st => st.Tickers.Contains(code)));
    }

    /// <summary>Tickers in the stages right before and after the ones that contain <paramref name="code"/>.</summary>
    public IReadOnlyList<string> SupplyChainNeighbours(string code)
    {
        code = Code(code);
        var result = new List<string>();
        foreach (var chain in SupplyChains)
            for (var i = 0; i < chain.Steps.Length; i++)
            {
                if (!chain.Steps[i].Tickers.Contains(code)) continue;
                foreach (var j in new[] { i - 1, i + 1 })
                    if (j >= 0 && j < chain.Steps.Length) result.AddRange(chain.Steps[j].Tickers);
            }
        return result.Where(t => t != code).Distinct().ToList();
    }

    /// <summary>Group peers, then factor peers and supply-chain neighbours: the curated "dots" around a ticker.</summary>
    public IReadOnlyList<string> RelatedTickers(string code)
    {
        code = Code(code);
        var factorPeers = Commodities.Where(c => c.Tickers.Contains(code)).SelectMany(c => c.Tickers)
            .Concat(Macro.Where(m => m.Tickers!.Contains(code)).SelectMany(m => m.Tickers!));
        return GroupsFor(code).SelectMany(g => g.Tickers).Concat(factorPeers).Concat(SupplyChainNeighbours(code))
            .Where(t => t != code).Distinct().ToList();
    }

    public bool Mentions(string code)
    {
        code = Code(code);
        return Groups.Any(g => g.Tickers.Contains(code)) || Commodities.Any(c => c.Tickers.Contains(code))
            || Macro.Any(m => m.Tickers!.Contains(code)) || SupplyChains.Any(s => s.Steps.Any(st => st.Tickers.Contains(code)));
    }

    static string Code(string code) => code.Trim().ToUpperInvariant();

    static string[] Norm(string[]? tickers) => (tickers ?? []).Select(Code).Distinct().ToArray();
}

/// <summary>Holds the current map and reloads Seed/relations.json when the file changes.</summary>
public sealed class RelationsMapProvider(ILogger<RelationsMapProvider> logger)
{
    readonly string _path = SeedPaths.File("relations.json");
    readonly Lock _lock = new();
    RelationsMap _map = RelationsMap.Empty;
    DateTime _stamp = DateTime.MinValue;

    public RelationsMap Current
    {
        get
        {
            var stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (stamp == _stamp) return _map;
            lock (_lock)
            {
                if (stamp == _stamp) return _map;
                try
                {
                    _map = stamp == DateTime.MinValue ? RelationsMap.Empty : RelationsMap.Load(_path);
                    logger.LogInformation("Loaded relations map ({Groups} groups, updated {Updated})", _map.Groups.Count, _map.Updated);
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    // Keep the previous map while the file is being edited or is invalid.
                    logger.LogWarning("relations.json could not be loaded: {Error}", ex.Message);
                }
                _stamp = stamp;
                return _map;
            }
        }
    }
}
