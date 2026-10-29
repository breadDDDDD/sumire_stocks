using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sumire.Api.Forecasting;
using Sumire.Api.Market;

namespace Sumire.Api.Tests.Forecasting.Core;

public class ForecastCommandTests
{
    [Theory]
    [InlineData(null, "actions")]
    [InlineData("", "actions")]
    [InlineData("  ", "actions")]
    [InlineData(" OFF ", "off")]
    [InlineData("Local", "local")]
    public void Mode_is_normalized(string? raw, string expected) => Assert.Equal(expected, ForecastCommand.Mode(raw));

    [Fact]
    public async Task Skip_holidays_uses_the_wib_date_and_exits_zero_before_touching_the_db()
    {
        // Friday 23:30 UTC is Saturday 06:30 WIB.
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero));
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(MarketCalendar.Load(IdxRules.Load(), clock: clock))
            .BuildServiceProvider();

        Assert.Equal(0, await ForecastCommand.RunOnceAsync(services, ["--once", "--skip-holidays"], CancellationToken.None));
    }
}
