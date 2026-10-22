using Sumire.Api.Market;

namespace Sumire.Api.Tests.Market;

public class MarketCalendarTests
{
    static readonly MarketCalendar Calendar = MarketCalendar.Load(IdxRules.Load());

    static DateTimeOffset Wib(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Weekends_are_not_trading_days()
    {
        Assert.False(Calendar.IsTradingDay(new DateOnly(2026, 10, 24))); // Saturday
        Assert.False(Calendar.IsTradingDay(new DateOnly(2026, 10, 25))); // Sunday
        Assert.True(Calendar.IsTradingDay(new DateOnly(2026, 10, 22)));  // Thursday
    }

    [Fact]
    public void Holidays_are_not_trading_days()
    {
        Assert.False(Calendar.IsTradingDay(new DateOnly(2026, 8, 17)));
        Assert.False(Calendar.IsTradingDay(new DateOnly(2026, 12, 31)));
        Assert.Equal("Hari Kemerdekaan Republik Indonesia", Calendar.HolidayOn(new DateOnly(2026, 8, 17))?.Name);
    }

    [Fact]
    public void Seed_holidays_cover_2026_and_2027_and_fall_on_weekdays()
    {
        Assert.Contains(Calendar.Holidays, h => h.Date.Year == 2026);
        Assert.Contains(Calendar.Holidays, h => h.Date.Year == 2027);
        Assert.All(Calendar.Holidays, h => Assert.True(h.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), h.Date.ToString()));
    }

    [Fact]
    public void Next_and_previous_trading_day_skip_weekends_and_holidays()
    {
        // Fri 2026-08-14 -> skip weekend and Mon 17 Aug -> Tue 18 Aug
        Assert.Equal(new DateOnly(2026, 8, 18), Calendar.NextTradingDay(new DateOnly(2026, 8, 14)));
        Assert.Equal(new DateOnly(2026, 8, 14), Calendar.PreviousTradingDay(new DateOnly(2026, 8, 18)));
        // Eid 2026: closed 18-24 March
        Assert.Equal(new DateOnly(2026, 3, 25), Calendar.NextTradingDay(new DateOnly(2026, 3, 17)));
        // Year end: 31 Dec 2026 closed, 1 Jan 2027 closed -> Mon 4 Jan 2027
        Assert.Equal(new DateOnly(2027, 1, 4), Calendar.NextTradingDay(new DateOnly(2026, 12, 30)));
    }

    [Theory]
    [InlineData(8, 30, MarketSession.Closed)]
    [InlineData(8, 45, MarketSession.PreOpen)]
    [InlineData(8, 59, MarketSession.PreOpen)]
    [InlineData(9, 0, MarketSession.Session1)]
    [InlineData(11, 59, MarketSession.Session1)]
    [InlineData(12, 0, MarketSession.Break)]
    [InlineData(13, 29, MarketSession.Break)]
    [InlineData(13, 30, MarketSession.Session2)]
    [InlineData(15, 49, MarketSession.Session2)]
    [InlineData(15, 50, MarketSession.PreClose)]
    [InlineData(16, 0, MarketSession.Closed)]
    [InlineData(20, 0, MarketSession.Closed)]
    public void Monday_to_Thursday_sessions(int hour, int minute, MarketSession expected) =>
        Assert.Equal(expected, Calendar.SessionAt(Wib(2026, 10, 22, hour, minute)));

    [Theory]
    [InlineData(11, 29, MarketSession.Session1)]
    [InlineData(11, 30, MarketSession.Break)]
    [InlineData(13, 45, MarketSession.Break)]
    [InlineData(14, 0, MarketSession.Session2)]
    [InlineData(15, 55, MarketSession.PreClose)]
    public void Friday_has_a_longer_break(int hour, int minute, MarketSession expected) =>
        Assert.Equal(expected, Calendar.SessionAt(Wib(2026, 10, 23, hour, minute)));

    [Fact]
    public void Holidays_and_weekends_are_closed_all_day()
    {
        Assert.Equal(MarketSession.Closed, Calendar.SessionAt(Wib(2026, 8, 17, 10, 0)));
        Assert.Equal(MarketSession.Closed, Calendar.SessionAt(Wib(2026, 10, 24, 10, 0)));
    }

    [Fact]
    public void Session_uses_wib_regardless_of_input_offset()
    {
        // 03:00 UTC = 10:00 WIB on a Thursday
        Assert.Equal(MarketSession.Session1, Calendar.SessionAt(new DateTimeOffset(2026, 10, 22, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Now_and_current_session_use_injected_clock()
    {
        var calendar = MarketCalendar.Load(IdxRules.Load(), clock: FakeClock.AtWib(2026, 10, 22, 10, 15));
        Assert.Equal(new DateOnly(2026, 10, 22), calendar.TodayWib);
        Assert.Equal(TimeSpan.FromHours(7), calendar.NowWib.Offset);
        Assert.Equal(MarketSession.Session1, calendar.CurrentSession);
        Assert.True(calendar.IsOpenNow);
    }
}
