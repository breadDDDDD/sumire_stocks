namespace Sumire.Api.Common;

/// <summary>Western Indonesia Time (Asia/Jakarta, UTC+7, no DST).</summary>
public static class Wib
{
    public static readonly TimeZoneInfo Zone = FindZone();

    static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Asia/Jakarta", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
    }

    public static DateTimeOffset ToWib(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone);

    public static DateTimeOffset Now(TimeProvider clock) => ToWib(clock.GetUtcNow());

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock).DateTime);

    /// <summary>ISO-8601 with the +07:00 offset, e.g. 2026-10-22T14:32:10+07:00.</summary>
    public static string Iso(DateTimeOffset time) => ToWib(time).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
}
