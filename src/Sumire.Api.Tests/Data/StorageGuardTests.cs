using Microsoft.Extensions.Logging.Abstractions;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Data;

namespace Sumire.Api.Tests.Data;

public class StorageGuardTests
{
    const long Mb = 1024 * 1024;

    static RetentionStep Step(string name) => new(name, name, (_, _, _) => Task.FromResult(0));

    /// <summary>Deleting only shrinks the measured size once the table is compacted, like real Postgres.</summary>
    sealed class FakeDb(long size, long compactFreesMb, Dictionary<string, (int Rows, long FreesMb)> freed)
    {
        long pending = compactFreesMb * Mb;
        public long Size { get; private set; } = size * Mb;
        public List<string> Log { get; } = [];

        public Task<long> Measure(CancellationToken _)
        {
            Log.Add("measure");
            return Task.FromResult(Size);
        }

        public Task<int> Delete(RetentionStep step)
        {
            Log.Add("delete " + step.Name);
            var (rows, frees) = freed.GetValueOrDefault(step.Name);
            pending += frees * Mb;
            return Task.FromResult(rows);
        }

        public Task Compact(IEnumerable<string> tables, CancellationToken _)
        {
            Log.Add("compact " + string.Join(",", tables));
            Size -= pending;
            pending = 0;
            return Task.CompletedTask;
        }

        public Task<long> Run(long limitMb, params RetentionStep[] steps) =>
            StorageGuard.EnforceAsync(limitMb * Mb, ["all"], steps, Measure, Delete, Compact, NullLogger.Instance, default);
    }

    [Fact]
    public async Task Under_the_limit_touches_nothing()
    {
        var db = new FakeDb(100, 0, []);
        await db.Run(250, Step("a"));
        Assert.Equal(["measure"], db.Log);
    }

    [Fact]
    public async Task Compaction_alone_can_be_enough()
    {
        var db = new FakeDb(300, 60, new() { ["a"] = (10, 10) });
        await db.Run(250, Step("a"));
        Assert.DoesNotContain("delete a", db.Log);
    }

    [Fact]
    public async Task Stops_at_the_first_step_that_gets_back_under()
    {
        var db = new FakeDb(300, 0, new() { ["a"] = (10, 20), ["b"] = (10, 40), ["c"] = (10, 40) });
        var size = await db.Run(250, Step("a"), Step("b"), Step("c"));
        Assert.Equal(240 * Mb, size);
        Assert.Contains("delete b", db.Log);
        Assert.DoesNotContain("delete c", db.Log);
    }

    [Fact]
    public async Task Compacts_a_step_table_before_measuring_it()
    {
        var db = new FakeDb(300, 0, new() { ["a"] = (10, 60) });
        await db.Run(250, Step("a"));
        Assert.Equal(["measure", "compact all", "measure", "delete a", "compact a", "measure"], db.Log);
    }

    [Fact]
    public async Task A_step_with_nothing_to_delete_skips_compaction()
    {
        var db = new FakeDb(300, 0, new() { ["b"] = (10, 60) });
        await db.Run(250, Step("a"), Step("b"));
        Assert.DoesNotContain("compact a", db.Log);
        Assert.Contains("compact b", db.Log);
    }

    [Fact]
    public async Task Stays_over_the_limit_when_every_step_is_used()
    {
        var db = new FakeDb(300, 0, new() { ["a"] = (10, 10) });
        Assert.Equal(290 * Mb, await db.Run(250, Step("a")));
    }

    [Fact]
    public async Task A_failing_compaction_stops_the_deletes()
    {
        var calls = new List<string>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StorageGuard.EnforceAsync(
            Mb, ["all"], [Step("a")], _ => Task.FromResult(2 * Mb),
            s => { calls.Add(s.Name); return Task.FromResult(1); },
            (_, _) => throw new InvalidOperationException("denied"), NullLogger.Instance, default));
        Assert.Equal("denied", ex.Message);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(null, 250)]
    [InlineData("", 250)]
    [InlineData("junk", 250)]
    [InlineData("-5", 250)]
    [InlineData("100", 100)]
    [InlineData("0", 0)]
    public void Limit_comes_from_the_environment_value(string? raw, long mb) =>
        Assert.Equal(mb * Mb, StorageGuard.LimitBytes(raw));

    [Fact]
    public void Floors_keep_what_the_code_reads_back()
    {
        Assert.True(StorageGuard.BarFloorDays > AnalyticsStore.HistoryDays);
        Assert.True(StorageGuard.ForecastFloorDays > 120, "a lower floor would retrigger the baseline backtest");
    }
}
