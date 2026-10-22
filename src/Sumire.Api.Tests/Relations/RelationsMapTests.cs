using Sumire.Api.Common;
using Sumire.Api.Relations;

namespace Sumire.Api.Tests.Relations;

public class RelationsMapTests
{
    const string Json = """
        {
          "updated": "2026-10-22",
          "groups": [
            { "name": "Djarum", "tickers": ["BBCA", "TOWR"], "note": "Hartono", "asOf": "2025-12" },
            { "name": "Danantara", "tickers": ["bbri", "BMRI", "PTBA"] }
          ],
          "commodities": [
            { "factor": "COAL", "proxySymbol": null, "tickers": ["PTBA", "ADRO"], "direction": "positive" },
            { "factor": "OIL", "proxySymbol": "BRENT", "tickers": ["MEDC"], "direction": "positive" },
            { "factor": "OIL", "proxySymbol": "BRENT", "tickers": ["GIAA"], "direction": "negative", "note": "fuel cost" }
          ],
          "macro": [
            { "factor": "BI_RATE", "tickers": ["BBRI", "BBCA"], "direction": "negative" },
            { "factor": "USDIDR", "tickers": ["PTBA"], "direction": "positive" },
            { "factor": "CHINA", "sectors": ["Energy"], "direction": "positive" }
          ],
          "supplyChains": [
            { "name": "Coal to power", "steps": [
              { "stage": "Contractors", "tickers": ["UNTR"] },
              { "stage": "Producers", "tickers": ["PTBA", "ADRO"] },
              { "stage": "Power", "tickers": ["PGEO"] } ] }
          ]
        }
        """;

    static readonly RelationsMap Map = RelationsMap.Parse(Json);

    [Fact]
    public void Groups_for_ticker_are_case_insensitive()
    {
        Assert.Equal(["Danantara"], Map.GroupsFor("bbri").Select(g => g.Name));
        Assert.Equal(["BBRI", "BMRI", "PTBA"], Map.Groups[1].Tickers);
        Assert.Equal("Hartono", Map.GroupsFor("TOWR").Single().Note);
    }

    [Fact]
    public void Factors_for_ticker_include_commodity_macro_and_sector_links()
    {
        var ptba = Map.FactorsFor("PTBA").ToList();
        Assert.Contains(ptba, f => f is { Factor: "COAL", Kind: "commodity", ProxyCode: null });
        Assert.Contains(ptba, f => f is { Factor: "USDIDR", Kind: "macro", ProxyCode: "USDIDR" });
        Assert.DoesNotContain(ptba, f => f.Factor == "CHINA");

        var bySector = Map.FactorsFor("ITMG", "Energy").Single();
        Assert.Equal(("CHINA", "SSE"), (bySector.Factor, bySector.ProxyCode));

        var giaa = Map.FactorsFor("GIAA").Single();
        Assert.Equal(("OIL", "negative", "BRENT"), (giaa.Factor, giaa.Direction, giaa.ProxyCode));
    }

    [Fact]
    public void Reverse_lookup_returns_every_entry_of_a_factor()
    {
        var oil = Map.Factor("oil");
        Assert.Equal(2, oil.Count);
        Assert.Equal(["MEDC"], oil[0].Tickers);
        Assert.Equal("negative", oil[1].Link.Direction);
        Assert.Equal(["BBRI", "BBCA"], Map.Factor("BI_RATE").Single().Tickers);
        Assert.Equal(["Energy"], Map.Factor("CHINA").Single().Sectors);
        Assert.Empty(Map.Factor("URANIUM"));
    }

    [Fact]
    public void Supply_chain_neighbours_are_adjacent_stages()
    {
        Assert.Equal(["UNTR", "PGEO"], Map.SupplyChainNeighbours("ADRO"));
        Assert.Equal(["PTBA", "ADRO"], Map.SupplyChainNeighbours("UNTR"));
        Assert.Equal("Coal to power", Map.SupplyChainsFor("PGEO").Single().Name);
    }

    [Fact]
    public void Related_tickers_combine_groups_factors_and_chain()
    {
        var related = Map.RelatedTickers("PTBA");
        Assert.Equal(["BBRI", "BMRI", "ADRO", "UNTR", "PGEO"], related);
        Assert.DoesNotContain("PTBA", related);
    }

    [Fact]
    public void Unknown_ticker_has_no_links()
    {
        Assert.False(Map.Mentions("ZZZZ"));
        Assert.Empty(Map.GroupsFor("ZZZZ"));
        Assert.Empty(Map.FactorsFor("ZZZZ"));
        Assert.Empty(Map.SupplyChainNeighbours("ZZZZ"));
        Assert.Empty(Map.RelatedTickers("ZZZZ"));
        Assert.True(Map.Mentions("pgeo"));
    }

    [Fact]
    public void Seed_file_loads()
    {
        var map = RelationsMap.Load(SeedPaths.File("relations.json"));
        Assert.NotEmpty(map.Groups);
        Assert.Contains(map.GroupsFor("BBCA"), g => g.Name == "Djarum");
        Assert.Contains(map.FactorsFor("ANTM"), f => f is { Factor: "GOLD", ProxyCode: "GOLD" });
        Assert.Contains("COAL", map.FactorNames);
    }
}
