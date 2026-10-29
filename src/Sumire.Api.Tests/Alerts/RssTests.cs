using Sumire.Api.Data;
using Sumire.Api.Market;
using Sumire.Api.Market.Rss;

namespace Sumire.Api.Tests.Alerts;

public class RssTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rss", name));

    [Theory]
    [InlineData("cnbc-market.xml", "CNBC Indonesia", 12)]
    [InlineData("kontan-investasi.xml", "Kontan", 12)]
    [InlineData("idxchannel-market.xml", "IDX Channel", 10)]
    public void Parses_feed_items(string file, string publisher, int count)
    {
        var items = RssParser.Parse(Fixture(file), publisher);

        Assert.Equal(count, items.Count);
        Assert.All(items, h =>
        {
            Assert.False(string.IsNullOrWhiteSpace(h.Title));
            Assert.StartsWith("https://", h.Url);
            Assert.Equal(publisher, h.Publisher);
            Assert.NotNull(h.PublishedAt);
            Assert.DoesNotContain("<", h.Summary ?? "");
            Assert.DoesNotContain("&quot;", h.Summary ?? "");
        });
    }

    [Fact]
    public void Dates_keep_the_wib_offset()
    {
        var first = RssParser.Parse(Fixture("cnbc-market.xml"), "CNBC Indonesia")[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 40, 0, TimeSpan.FromHours(7)), first.PublishedAt);
        Assert.Equal("OJK Ungkap Minat IPO Tidak Turun, Ada Bank Ngantre di Pipeline", first.Title);
    }

    [Fact]
    public void Site_wide_feed_is_filtered_to_market_links()
    {
        var items = RssParser.Parse(Fixture("bisnis-all.xml"), "Bisnis", "market.bisnis.com");
        Assert.NotEmpty(items);
        Assert.All(items, h => Assert.Contains("market.bisnis.com", h.Url));
    }

    [Theory]
    [InlineData("Thu, 22 Oct 2026 20:40:00 +0700", 13)]
    [InlineData("Thu, 22 Oct 2026 13:40:00 GMT", 13)]
    [InlineData("2026-10-22T20:40:00+07:00", 13)]
    [InlineData("2026-10-22 20:40:00", 13)] // no offset: WIB, whatever the machine's time zone
    public void Parses_rss_date_formats(string s, int utcHour) =>
        Assert.Equal(utcHour, RssParser.ParseDate(s)!.Value.UtcDateTime.Hour);

    static readonly TickerMatcher Matcher = new([
        new Instrument { Code = "BBRI", YahooSymbol = "BBRI.JK", Name = "PT Bank Rakyat Indonesia (Persero) Tbk" },
        new Instrument { Code = "BMRI", YahooSymbol = "BMRI.JK", Name = "PT Bank Mandiri (Persero) Tbk" },
        new Instrument { Code = "ANTM", YahooSymbol = "ANTM.JK", Name = "PT Aneka Tambang Tbk" },
        new Instrument { Code = "GOLD", YahooSymbol = "GC=F", Name = "Gold Futures", IsIndex = true },
    ]);

    [Theory]
    [InlineData("BRI (BBRI) Siapkan Buyback Saham Rp1 Triliun", new[] { "BBRI" })]
    [InlineData("Prospek Saham ANTM dan BMRI Versi Analis", new[] { "ANTM", "BMRI" })]
    [InlineData("Laba Bank Mandiri naik", new[] { "BMRI" })]
    [InlineData("Pasar diguncang MSCI, OJK buka suara", new string[0])]
    [InlineData("antm lowercase is not a code; GOLD is an index", new string[0])]
    [InlineData("XBBRIX is not a word match", new string[0])]
    public void Matches_codes_and_short_names(string text, string[] expected) =>
        Assert.Equal(expected, Matcher.Match(text));

    [Fact]
    public void Short_name_drops_pt_persero_and_tbk() =>
        Assert.Equal("Bank Rakyat Indonesia", TickerMatcher.ShortName("PT Bank Rakyat Indonesia (Persero) Tbk"));

    [Fact]
    public void Real_feed_items_are_tagged_from_the_seed_universe()
    {
        var matcher = new TickerMatcher(InstrumentCatalog.FromSeed().All);
        var tagged = RssParser.Parse(Fixture("idxchannel-market.xml"), "IDX Channel").Select(matcher.Tag).ToList();

        Assert.Contains(tagged, h => h.Tickers.Contains("BBRI"));
        Assert.Contains(tagged, h => h.Tickers.Contains("BMRI"));
    }

    [Fact]
    public void Merge_dedupes_by_url_and_sorts_newest_first()
    {
        var t = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(7));
        var merged = RssNewsClient.Merge([
            new Headline("a", "https://x/1", "P", t, null, []),
            new Headline("b", "https://x/2", "P", t.AddHours(1), null, []),
            new Headline("a again", "https://x/1/", "Q", t, null, []),
        ]);
        Assert.Equal(["b", "a"], merged.Select(h => h.Title));
    }
}
