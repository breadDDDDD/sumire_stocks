using Microsoft.Extensions.Logging.Abstractions;
using Sumire.Api.Analytics.Core;
using Sumire.Api.Relations;
using Sumire.Api.Tests.Analytics;

namespace Sumire.Api.Tests.Relations;

public class StatisticalLinksTests
{
    /// <summary>Price series built from daily log returns, starting at 1000.</summary>
    static List<Bar> FromReturns(IEnumerable<double> returns)
    {
        var p = 1000.0;
        return SyntheticBars.FromCloses(new[] { 0.0 }.Concat(returns).Select(r => p *= Math.Exp(r)).ToList());
    }

    static double[] Noise(int n, int seed, double sigma = 0.02)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => sigma * SyntheticBars.Gaussian(rng)).ToArray();
    }

    static List<Bar> WithMarket(double[] market, int seed, double sigma) =>
        FromReturns(market.Zip(Noise(market.Length, seed, sigma), (m, e) => m + e));

    [Fact]
    public void Pearson_matches_known_values()
    {
        Assert.Equal(1, StatisticalLinks.Pearson([1, 2, 3, 4], [2, 4, 6, 8]), 10);
        Assert.Equal(-1, StatisticalLinks.Pearson([1, 2, 3, 4], [8, 6, 4, 2]), 10);
        Assert.True(double.IsNaN(StatisticalLinks.Pearson([1, 1, 1], [1, 2, 3])));
        Assert.True(double.IsNaN(StatisticalLinks.Pearson([1, 2], [1, 2])));
    }

    [Fact]
    public void Correlation_of_shared_driver_is_high_and_independent_is_low()
    {
        var market = Noise(100, 1);
        var a = WithMarket(market, 2, 0.005);
        var b = WithMarket(market, 3, 0.005);
        var c = FromReturns(Noise(100, 4));

        var ab = StatisticalLinks.Correlation(a, b)!.Value;
        Assert.Equal(60, ab.N);
        Assert.True(ab.Rho > 0.85, $"rho {ab.Rho}");
        Assert.True(Math.Abs(StatisticalLinks.Correlation(a, c)!.Value.Rho) < 0.4);
    }

    [Fact]
    public void Correlation_needs_enough_overlap()
    {
        var a = FromReturns(Noise(30, 1));
        Assert.Null(StatisticalLinks.Correlation(a, a));
        Assert.Null(StatisticalLinks.Correlation(a, []));
    }

    [Fact]
    public void Clusters_keep_strong_links_both_ways_sorted_by_strength()
    {
        var market = Noise(100, 10);
        Dictionary<string, IReadOnlyList<Bar>> stocks = new()
        {
            ["AAAA"] = WithMarket(market, 11, 0.004),
            ["BBBB"] = WithMarket(market, 12, 0.008),
            ["CCCC"] = FromReturns(market.Select(m => -m)),
            ["DDDD"] = FromReturns(Noise(100, 13)),
        };

        var clusters = StatisticalLinks.CorrelationClusters(stocks);

        Assert.False(clusters.ContainsKey("DDDD"));
        Assert.Equal(["CCCC", "BBBB"], clusters["AAAA"].Select(l => l.Code));
        Assert.True(clusters["AAAA"][0].Rho < -0.9);
        Assert.Equal(clusters["AAAA"].Single(l => l.Code == "BBBB").Rho, clusters["BBBB"].Single(l => l.Code == "AAAA").Rho);
    }

    [Fact]
    public void Clusters_cap_at_top_five()
    {
        var market = Noise(100, 20);
        var stocks = Enumerable.Range(0, 8).ToDictionary(i => $"S{i:000}", i => (IReadOnlyList<Bar>)WithMarket(market, 100 + i, 0.003));
        var clusters = StatisticalLinks.CorrelationClusters(stocks);
        Assert.Equal(8, clusters.Count);
        Assert.All(clusters.Values, l => Assert.Equal(StatisticalLinks.TopCorrelated, l.Count));
    }

    [Fact]
    public void Lead_lag_detects_stock_following_previous_day_proxy()
    {
        var proxyReturns = Noise(150, 30);
        var proxy = FromReturns(proxyReturns);
        var noise = Noise(150, 31, 0.01);
        // Stock return today = 0.8 x proxy return yesterday + noise.
        var follower = FromReturns(Enumerable.Range(0, 150).Select(i => (i > 0 ? 0.8 * proxyReturns[i - 1] : 0) + noise[i]));
        var unrelated = FromReturns(Noise(150, 32));

        var link = StatisticalLinks.LeadLag(follower, proxy, "GOLD")!;
        Assert.Equal(120, link.N);
        Assert.True(link.Rho > 0.6, $"rho {link.Rho}");
        Assert.True(link.TStat > StatisticalLinks.MinTStat);

        // The same-day correlation is weak: the lag is what links them.
        Assert.True(Math.Abs(StatisticalLinks.Correlation(follower, proxy, 120)!.Value.Rho) < 0.3);

        var all = StatisticalLinks.LeadLags(
            new Dictionary<string, IReadOnlyList<Bar>> { ["FOLW"] = follower, ["UNRL"] = unrelated },
            new Dictionary<string, IReadOnlyList<Bar>> { ["GOLD"] = proxy });
        Assert.Equal("GOLD", all["FOLW"].Single().Proxy);
        Assert.False(all.ContainsKey("UNRL"));
    }

    [Fact]
    public void TStat_handles_edges()
    {
        Assert.Equal(0, StatisticalLinks.TStat(0.5, 2));
        Assert.Equal(99, StatisticalLinks.TStat(1, 50));
        Assert.Equal(0.5 * Math.Sqrt(98 / 0.75), StatisticalLinks.TStat(0.5, 100), 6);
    }

    [Fact]
    public void Store_round_trips_through_the_cache_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sumire-rel-{Guid.NewGuid():N}.json");
        try
        {
            var snapshot = new StatisticalSnapshot(new DateOnly(2026, 10, 22), DateTimeOffset.UnixEpoch,
                new() { ["BBCA"] = [new CorrelatedLink("BBRI", 0.71, 60)] },
                new() { ["ANTM"] = [new LeadLagLink("GOLD", 0.31, 120, 3.5)] });
            new RelationsStatsStore(NullLogger<RelationsStatsStore>.Instance, path).Save(snapshot);

            var loaded = new RelationsStatsStore(NullLogger<RelationsStatsStore>.Instance, path);
            Assert.True(loaded.HasData);
            Assert.Equal(snapshot.AsOf, loaded.Snapshot.AsOf);
            Assert.Equal("BBRI", loaded.Snapshot.Correlated["BBCA"].Single().Code);
            Assert.Equal(3.5, loaded.Snapshot.LeadLag["ANTM"].Single().TStat);

            Assert.False(new RelationsStatsStore(NullLogger<RelationsStatsStore>.Instance, path + ".missing").HasData);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
