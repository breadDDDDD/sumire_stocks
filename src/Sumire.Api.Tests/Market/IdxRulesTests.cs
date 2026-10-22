using Sumire.Api.Market;

namespace Sumire.Api.Tests.Market;

public class IdxRulesTests
{
    static readonly IdxRules Rules = IdxRules.Load();

    [Theory]
    [InlineData(50, 1)]
    [InlineData(199, 1)]
    [InlineData(200, 2)]
    [InlineData(498, 2)]
    [InlineData(500, 5)]
    [InlineData(1995, 5)]
    [InlineData(2000, 10)]
    [InlineData(4990, 10)]
    [InlineData(5000, 25)]
    [InlineData(9875, 25)]
    public void TickSize_follows_price_bands(decimal price, decimal tick) =>
        Assert.Equal(tick, Rules.TickSize(price));

    [Theory]
    [InlineData(201, 202)]
    [InlineData(1233, 1235)]
    [InlineData(1232, 1230)]
    [InlineData(9880, 9875)]
    [InlineData(9890, 9900)]
    public void RoundToTick_snaps_to_nearest_valid_price(decimal price, decimal expected) =>
        Assert.Equal(expected, Rules.RoundToTick(price));

    [Fact]
    public void Lot_and_fees_come_from_rules()
    {
        Assert.Equal(100, Rules.LotSize);
        Assert.Equal(0.0015m, Rules.BuyFee);
        Assert.Equal(0.0025m, Rules.SellFee);
        Assert.Equal(915_000m, Rules.LotValue(9150));
    }

    [Fact]
    public void AraArb_symmetric_regime_rounds_inward_to_ticks()
    {
        var day = new DateOnly(2027, 2, 1);
        // 9,150 * 1.20 = 10,980 (tick 25 -> 10,975); 9,150 * 0.80 = 7,320 (tick 25 -> 7,325)
        Assert.Equal((10_975m, 7_325m), Rules.AraArb(9150, day));
        // 1,000 * 1.25 = 1,250; 1,000 * 0.75 = 750
        Assert.Equal((1_250m, 750m), Rules.AraArb(1000, day));
        // 100 * 1.35 = 135; 100 * 0.65 = 65
        Assert.Equal((135m, 65m), Rules.AraArb(100, day));
        // 1,234 * 1.25 = 1,542.5 -> 1,540; 1,234 * 0.75 = 925.5 -> 930
        Assert.Equal((1_540m, 930m), Rules.AraArb(1234, day));
    }

    [Fact]
    public void AraArb_band_edges_use_previous_close()
    {
        var day = new DateOnly(2027, 2, 1);
        Assert.Equal(270m, Rules.AraArb(200, day).Upper);   // 200 is in the 35% group
        Assert.Equal(6_250m, Rules.AraArb(5000, day).Upper); // 5,000 is in the 25% group
    }

    [Fact]
    public void AraArb_uses_dated_regime()
    {
        // Oct 2026: ARB cut to 7%, ARA unchanged at 20% above Rp5,000.
        Assert.Equal((10_975m, 8_525m), Rules.AraArb(9150, new DateOnly(2026, 10, 22)));
        // Early Oct 2026: ARB 15%.
        Assert.Equal((10_975m, 7_800m), Rules.AraArb(9150, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void AraArb_small_prices_move_by_one_rupiah_and_never_below_minimum()
    {
        var day = new DateOnly(2026, 10, 22);
        Assert.Equal((6m, 4m), Rules.AraArb(5, day));
        Assert.Equal((2m, 1m), Rules.AraArb(1, day));
    }

    [Fact]
    public void AraArb_defaults_to_today_wib()
    {
        var rules = IdxRules.Load(clock: FakeClock.AtWib(2027, 3, 1, 10));
        Assert.Equal(7_325m, rules.AraArb(9150).Lower);
    }

    [Fact]
    public void Costs_include_fees()
    {
        Assert.Equal(1_001_500m, Rules.BuyCost(1000, 10));
        Assert.Equal(997_500m, Rules.SellProceeds(1000, 10));
    }
}
