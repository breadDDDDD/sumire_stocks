using System.Text.Json;
using Sumire.Api.Common;

namespace Sumire.Api.Market;

public enum MarketSession { Closed, PreOpen, Session1, Break, Session2, PreClose }

public sealed record Holiday(DateOnly Date, string Name, bool Verified = true);

/// <summary>IDX trading days and session state in WIB, from idx-holidays.json and the session times in idx-rules.json.</summary>
public sealed class MarketCalendar
{
    readonly IdxRules _rules;
    readonly TimeProvider _clock;
    readonly Dictionary<DateOnly, Holiday> _holidays;

    public MarketCalendar(IdxRules rules, IEnumerable<Holiday> holidays, TimeProvider? clock = null)
    {
        _rules = rules;
        _clock = clock ?? TimeProvider.System;
        _holidays = holidays.ToDictionary(h => h.Date);
    }

    public static MarketCalendar Load(IdxRules rules, string? holidaysPath = null, TimeProvider? clock = null)
    {
        holidaysPath ??= SeedPaths.File("idx-holidays.json");
        var holidays = JsonSerializer.Deserialize<List<Holiday>>(File.ReadAllText(holidaysPath), IdxRules.JsonOptions) ?? [];
        return new MarketCalendar(rules, holidays, clock);
    }

    public DateTimeOffset NowWib => Wib.Now(_clock);
    public DateOnly TodayWib => Wib.Today(_clock);

    public IReadOnlyCollection<Holiday> Holidays => _holidays.Values;

    public Holiday? HolidayOn(DateOnly date) => _holidays.GetValueOrDefault(date);

    public bool IsTradingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_holidays.ContainsKey(date);

    public DateOnly NextTradingDay(DateOnly date)
    {
        do date = date.AddDays(1); while (!IsTradingDay(date));
        return date;
    }

    public DateOnly PreviousTradingDay(DateOnly date)
    {
        do date = date.AddDays(-1); while (!IsTradingDay(date));
        return date;
    }

    /// <summary>Session times for a weekday (Friday has a longer break).</summary>
    public DaySessions SessionsFor(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Friday ? _rules.File.Sessions.Fri : _rules.File.Sessions.MonThu;

    public MarketSession SessionAt(DateTimeOffset time)
    {
        var wib = Wib.ToWib(time);
        var date = DateOnly.FromDateTime(wib.DateTime);
        if (!IsTradingDay(date)) return MarketSession.Closed;

        var t = TimeOnly.FromDateTime(wib.DateTime);
        var s = SessionsFor(date);
        if (In(t, s.PreOpen)) return MarketSession.PreOpen;
        if (In(t, s.Session1)) return MarketSession.Session1;
        if (t >= DaySessions.Hm(s.Session1[1]) && t < DaySessions.Hm(s.Session2[0])) return MarketSession.Break;
        if (In(t, s.Session2)) return MarketSession.Session2;
        if (In(t, s.PreClose)) return MarketSession.PreClose;
        return MarketSession.Closed;
    }

    public MarketSession CurrentSession => SessionAt(_clock.GetUtcNow());

    /// <summary>True while orders can trade or queue (pre-open through pre-close, excluding the lunch break).</summary>
    public static bool IsOpen(MarketSession s) => s is MarketSession.PreOpen or MarketSession.Session1 or MarketSession.Session2 or MarketSession.PreClose;

    public bool IsOpenNow => IsOpen(CurrentSession);

    static bool In(TimeOnly t, string[] range) => t >= DaySessions.Hm(range[0]) && t < DaySessions.Hm(range[1]);
}
