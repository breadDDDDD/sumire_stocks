using Sumire.Api.Common;
using Sumire.Api.Market;

namespace Sumire.Api.Research;

public enum BatchKind { Morning, Midday, Close, Weekend }

public sealed record BatchOccurrence(BatchKind Kind, DateOnly Date, DateTimeOffset At)
{
    /// <summary>Lock-row subject, e.g. "close:2026-10-22".</summary>
    public string Id => $"{Kind.ToString().ToLowerInvariant()}:{Date:yyyy-MM-dd}";
}

/// <summary>Research batch times (WIB): 07:45, 12:05 (opt-in), 16:45 on trading days; Sunday 19:00 week-ahead.</summary>
public static class ResearchSchedule
{
    public static readonly TimeOnly MorningAt = new(7, 45);
    public static readonly TimeOnly MiddayAt = new(12, 5);
    public static readonly TimeOnly CloseAt = new(16, 45);
    public static readonly TimeOnly WeekendAt = new(19, 0);

    /// <summary>A missed batch older than this is dropped instead of run late.</summary>
    public static readonly TimeSpan MaxLateness = TimeSpan.FromHours(16);

    public static IEnumerable<BatchOccurrence> OccurrencesOn(DateOnly date, MarketCalendar calendar, bool midday)
    {
        if (calendar.IsTradingDay(date))
        {
            yield return At(BatchKind.Morning, date, MorningAt);
            if (midday) yield return At(BatchKind.Midday, date, MiddayAt);
            yield return At(BatchKind.Close, date, CloseAt);
        }
        // The weekend brief runs even when the Sunday is next to a holiday.
        if (date.DayOfWeek == DayOfWeek.Sunday) yield return At(BatchKind.Weekend, date, WeekendAt);
    }

    static BatchOccurrence At(BatchKind kind, DateOnly date, TimeOnly time) =>
        new(kind, date, new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(7)));

    /// <summary>
    /// The batch to run now: the most recent occurrence that is due (≤ now, at most MaxLateness old) and not yet run.
    /// Only the latest one counts, so older missed batches are never back-filled.
    /// </summary>
    public static BatchOccurrence? Due(DateTimeOffset now, MarketCalendar calendar, bool midday, ISet<string> alreadyRun)
    {
        var today = DateOnly.FromDateTime(Wib.ToWib(now).DateTime);
        var latest = Enumerable.Range(0, 3)
            .SelectMany(back => OccurrencesOn(today.AddDays(-back), calendar, midday))
            .Where(o => o.At <= now && now - o.At <= MaxLateness)
            .MaxBy(o => o.At);
        return latest is null || alreadyRun.Contains(latest.Id) ? null : latest;
    }

    /// <summary>The next occurrence of each batch kind after now (for the RQ screen).</summary>
    public static List<BatchOccurrence> Upcoming(DateTimeOffset now, MarketCalendar calendar, bool midday)
    {
        var today = DateOnly.FromDateTime(Wib.ToWib(now).DateTime);
        return Enumerable.Range(0, 15)
            .SelectMany(d => OccurrencesOn(today.AddDays(d), calendar, midday))
            .Where(o => o.At > now)
            .GroupBy(o => o.Kind)
            .Select(g => g.First())
            .OrderBy(o => o.At)
            .ToList();
    }
}
