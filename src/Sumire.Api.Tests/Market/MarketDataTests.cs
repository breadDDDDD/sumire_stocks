using Sumire.Api.Data;
using Sumire.Api.Jobs;
using Sumire.Api.Market;

namespace Sumire.Api.Tests.Market;

public class InstrumentCsvTests
{
    [Fact]
    public void Parses_columns_quotes_and_memberships()
    {
        const string csv = """
            code,yahoo_symbol,name,sector,industry,board,is_index,index_memberships,group_name,core
            BBCA,BBCA.JK,"PT Bank Central Asia, Tbk",Financials,Banks,Utama,false,LQ45|IDX30,Djarum,true
            IHSG,^JKSE,IDX Composite (IHSG),Index,Indonesia,,true,,,true

            """;
        var list = InstrumentCsv.Parse(csv.Replace("\n", "\r\n"));

        Assert.Equal(2, list.Count);
        var bbca = list[0];
        Assert.Equal("PT Bank Central Asia, Tbk", bbca.Name);
        Assert.Equal(["LQ45", "IDX30"], bbca.IndexMemberships);
        Assert.Equal("Djarum", bbca.GroupName);
        Assert.True(bbca.Core);
        Assert.False(bbca.IsIndex);
        Assert.True(list[1].IsIndex);
        Assert.Null(list[1].Board);
        Assert.Empty(list[1].IndexMemberships);
    }

    [Fact]
    public void Missing_column_is_reported()
    {
        var ex = Assert.Throws<InvalidDataException>(() => InstrumentCsv.Parse("code,name\nBBCA,x\n"));
        Assert.Contains("yahoo_symbol", ex.Message);
    }

    [Fact]
    public void Seed_file_loads()
    {
        var catalog = InstrumentCatalog.FromSeed();
        Assert.True(catalog.All.Count > 100);
        Assert.True(catalog.Find("IHSG")!.IsIndex);
        Assert.Equal("^JKSE", catalog.Find("ihsg")!.YahooSymbol);
        Assert.Equal("BBCA", catalog.FindBySymbol("BBCA.JK")!.Code);
        Assert.Contains(catalog.Tape, i => i.Code == "USDIDR");
    }
}

public class InstrumentCatalogTests
{
    static readonly InstrumentCatalog Catalog = new([
        new Instrument { Code = "BBCA", YahooSymbol = "BBCA.JK", Name = "Bank Central Asia", Core = true },
        new Instrument { Code = "BBRI", YahooSymbol = "BBRI.JK", Name = "Bank Rakyat Indonesia", Core = true },
        new Instrument { Code = "BBKP", YahooSymbol = "BBKP.JK", Name = "KB Bank", Core = false },
        new Instrument { Code = "ABBA", YahooSymbol = "ABBA.JK", Name = "Mahaka Media", Core = false },
        new Instrument { Code = "TLKM", YahooSymbol = "TLKM.JK", Name = "Telkom Indonesia", Core = true },
    ]);

    [Fact]
    public void Exact_code_then_prefix_with_core_first()
    {
        Assert.Equal(["BBCA", "BBRI", "BBKP"], Catalog.Search("bb").Select(i => i.Code));
        Assert.Equal("BBCA", Catalog.Search("BBCA")[0].Code);
    }

    [Fact]
    public void Name_matches_rank_after_codes()
    {
        Assert.Equal(["BBCA", "BBRI", "BBKP"], Catalog.Search("bank").Select(i => i.Code));
        Assert.Equal(["TLKM"], Catalog.Search("telkom").Select(i => i.Code));
    }

    [Fact]
    public void Limit_is_applied() => Assert.Single(Catalog.Search("b", 1));
}

public class QuoteTests
{
    static readonly Instrument Bbca = new() { Code = "BBCA", YahooSymbol = "BBCA.JK", Name = "Bank Central Asia" };

    [Fact]
    public void Close_quote_uses_last_two_bars()
    {
        var q = MarketDataService.CloseQuote(Bbca,
            new DailyBar { Code = "BBCA", Date = new DateOnly(2026, 10, 22), Open = 5125, High = 5275, Low = 5100, Close = 5175, Volume = 1000 },
            new DailyBar { Code = "BBCA", Date = new DateOnly(2026, 10, 21), Close = 5275 });

        Assert.Equal(-100m, q.Change);
        Assert.Equal(-1.8957m, q.ChangePct);
        Assert.Equal(5_175_000m, q.Value);
        Assert.Equal("close", q.Source);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.FromHours(7)), q.Time);
    }

    [Fact]
    public void Live_quote_maps_yahoo_fields_and_converts_time_to_wib()
    {
        var y = YahooParser.ParseQuotes(YahooParserTests.Fixture("quote-bbca-jkse-idr.json"));
        var q = MarketDataService.ToQuote(Bbca, y[0]);

        Assert.Equal(5175m, q.Price);
        Assert.Equal(-100m, q.Change);
        Assert.Equal("live", q.Source);
        Assert.Equal(TimeSpan.FromHours(7), q.Time!.Value.Offset);
        Assert.Equal(5175m * 286_928_500L, q.Value);
    }
}

public class JobScheduleTests
{
    [Theory]
    [InlineData(MarketSession.Session1, 1, true)]
    [InlineData(MarketSession.PreOpen, 2, true)]
    [InlineData(MarketSession.Session2, 1, true)]
    [InlineData(MarketSession.PreClose, 1, true)]
    [InlineData(MarketSession.Session1, 0, false)]
    [InlineData(MarketSession.Break, 1, false)]
    [InlineData(MarketSession.Closed, 3, false)]
    public void Intraday_polls_only_when_open_and_watched(MarketSession session, int connections, bool expected) =>
        Assert.Equal(expected, IntradayQuoteJob.ShouldPoll(session, connections));

    [Fact]
    public async Task Intraday_poll_does_nothing_without_connections()
    {
        var calendar = MarketCalendar.Load(IdxRules.Load(), clock: FakeClock.AtWib(2026, 10, 22, 10, 0));
        var job = new IntradayQuoteJob(null!, new QuoteSubscriptions(), InstrumentCatalog.FromSeed(), calendar, new MarketStatus(), null!,
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<IntradayQuoteJob>.Instance);

        Assert.Equal(0, await job.PollOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Intraday_poll_does_nothing_when_closed()
    {
        var calendar = MarketCalendar.Load(IdxRules.Load(), clock: FakeClock.AtWib(2026, 10, 24, 10, 0)); // Saturday
        var subs = new QuoteSubscriptions();
        subs.Subscribe("c1", ["BBCA"]);
        var job = new IntradayQuoteJob(null!, subs, InstrumentCatalog.FromSeed(), calendar, new MarketStatus(), null!,
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<IntradayQuoteJob>.Instance);

        Assert.Equal(0, await job.PollOnceAsync(CancellationToken.None));
    }

    static EodPipelineJob Eod() => new(null!, InstrumentCatalog.FromSeed(), MarketCalendar.Load(IdxRules.Load()), new MarketStatus(),
        TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<EodPipelineJob>.Instance);

    static DateTimeOffset Wib(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData(2026, 10, 22, 10, 0, 2026, 10, 22)] // Thu morning -> same day
    [InlineData(2026, 10, 22, 17, 0, 2026, 10, 23)] // Thu evening -> Fri
    [InlineData(2026, 10, 23, 16, 30, 2026, 10, 26)] // Fri at run time -> Mon
    [InlineData(2026, 10, 24, 9, 0, 2026, 10, 26)] // Sat -> Mon
    [InlineData(2026, 8, 14, 18, 0, 2026, 8, 18)] // Fri before Independence Day -> Tue
    public void Eod_next_run_is_1630_on_next_trading_day(int y, int m, int d, int h, int min, int ey, int em, int ed) =>
        Assert.Equal(Wib(ey, em, ed, 16, 30), Eod().NextRun(Wib(y, m, d, h, min)));
}

public class SubscriptionTests
{
    [Fact]
    public void Tracks_connections_and_codes()
    {
        var subs = new QuoteSubscriptions();
        subs.Connect("a");
        subs.Subscribe("a", ["bbca", "BBRI", " "]);
        subs.Subscribe("b", ["TLKM"]);
        subs.Unsubscribe("a", ["BBRI"]);

        Assert.Equal(2, subs.ConnectionCount);
        Assert.Equal(["BBCA"], subs.CodesFor("a"));
        Assert.Equal(new HashSet<string> { "BBCA", "TLKM" }, subs.AllCodes());

        subs.Disconnect("a");
        Assert.Equal(1, subs.ConnectionCount);
    }
}

public class WeeklyBarsTests
{
    [Fact]
    public void Aggregates_monday_to_friday()
    {
        var days = new[] { (19, 100m), (20, 110m), (23, 105m), (26, 120m), (27, 118m) } // Mon 19 … Fri 23, Mon 26, Tue 27 Oct 2026
            .Select(x => new MarketEndpoints.ChartBar(0, $"2026-10-{x.Item1:00}", x.Item2, x.Item2 + 5, x.Item2 - 5, x.Item2 + 1, 10))
            .ToList();

        var weeks = MarketEndpoints.Weekly(days);

        Assert.Equal(2, weeks.Count);
        Assert.Equal(("2026-10-19", 100m, 115m, 95m, 106m, 30L), (weeks[0].Date, weeks[0].Open, weeks[0].High, weeks[0].Low, weeks[0].Close, weeks[0].Volume));
        Assert.Equal(119m, weeks[1].Close);
    }
}
