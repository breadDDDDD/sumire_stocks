namespace Sumire.Api.Tests;

/// <summary>Fixed clock for tests; set with a WIB wall time.</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public static FakeClock AtWib(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(7)));
}
