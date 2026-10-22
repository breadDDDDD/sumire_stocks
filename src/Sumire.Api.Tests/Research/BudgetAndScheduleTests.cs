using Microsoft.Extensions.Configuration;
using Sumire.Api.Market;
using Sumire.Api.Research;

namespace Sumire.Api.Tests.Research;

public class BudgetTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(7));
    static readonly HashSet<string> Known = ["agy", "claude-code"];

    static ResearchOptions Options(string runners = "agy:gemini-flash, agy:claude-sonnet, claude-code:sonnet", string caps = "", int daily = 20) => new()
    {
        Runners = ResearchOptions.ParseRunners(runners),
        ModelCaps = ResearchOptions.ParseModelCaps(caps),
        DailyCap = daily,
    };

    static ResearchUsage Usage(params (string Key, int N)[] rows) => new(rows.ToDictionary(r => r.Key, r => r.N));

    [Fact]
    public void Parses_runner_list_and_model_caps()
    {
        var runners = ResearchOptions.ParseRunners(" agy:gemini-3.8-flash-medium , claude-code:sonnet;gemini-cli");
        Assert.Equal(["agy:gemini-3.8-flash-medium", "claude-code:sonnet", "gemini-cli:"], runners.Select(r => r.Key));

        var caps = ResearchOptions.ParseModelCaps("claude-*:6, agy:gemini-*:15, bad, x:y");
        Assert.Equal(2, caps.Count);
        Assert.True(caps[0].Matches(new RunnerEntry("agy", "claude-sonnet-4-6")));
        Assert.False(caps[0].Matches(new RunnerEntry("agy", "gemini-flash")));
        Assert.True(caps[1].Matches(new RunnerEntry("agy", "gemini-flash")));
        Assert.False(caps[1].Matches(new RunnerEntry("gemini-cli", "gemini-flash")));
    }

    [Fact]
    public void Options_from_config_fall_back_to_defaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RESEARCH_RUNNERS"] = "", ["RESEARCH_DAILY_CAP"] = "12", ["RESEARCH_ENABLED"] = "false",
        }).Build();
        var o = ResearchOptions.FromConfig(config);
        Assert.Equal(ResearchOptions.DefaultRunners, o.Runners);
        Assert.Contains(o.Runners, r => r.Runner == "claude-code");
        Assert.Equal(12, o.DailyCap);
        Assert.Equal(8, o.BatchCap);
        Assert.False(o.Enabled);
    }

    [Fact]
    public void Picks_first_entry_in_order()
    {
        var pick = ResearchBudget.Pick(Options(), Known, ResearchUsage.Empty, new Dictionary<string, DateTimeOffset>(), Now);
        Assert.Equal("agy:gemini-flash", pick.Entry!.Key);
    }

    [Fact]
    public void Falls_back_when_exhausted_until_the_reset_passes()
    {
        var exhausted = new Dictionary<string, DateTimeOffset> { ["agy:gemini-flash"] = Now.AddHours(2) };
        Assert.Equal("agy:claude-sonnet", ResearchBudget.Pick(Options(), Known, ResearchUsage.Empty, exhausted, Now).Entry!.Key);
        Assert.Equal("agy:gemini-flash", ResearchBudget.Pick(Options(), Known, ResearchUsage.Empty, exhausted, Now.AddHours(3)).Entry!.Key);
    }

    [Fact]
    public void Model_cap_is_a_shared_pool_across_runners()
    {
        var options = Options("agy:claude-sonnet, claude-code:claude-sonnet, agy:gemini-flash", caps: "claude-*:3");
        var usage = Usage(("agy:claude-sonnet", 2), ("claude-code:claude-sonnet", 1));
        Assert.Equal("agy:gemini-flash", ResearchBudget.Pick(options, Known, usage, new Dictionary<string, DateTimeOffset>(), Now).Entry!.Key);
    }

    [Fact]
    public void Daily_cap_blocks_everything()
    {
        var pick = ResearchBudget.Pick(Options(daily: 3), Known, Usage(("agy:gemini-flash", 3)), new Dictionary<string, DateTimeOffset>(), Now);
        Assert.Null(pick.Entry);
        Assert.Contains("daily cap", pick.BlockedReason);
    }

    [Fact]
    public void Unknown_runners_and_skipped_entries_are_passed_over_then_all_exhausted()
    {
        var options = Options("codex:x, agy:gemini-flash");
        Assert.Equal("agy:gemini-flash", ResearchBudget.Pick(options, Known, ResearchUsage.Empty, new Dictionary<string, DateTimeOffset>(), Now).Entry!.Key);
        var none = ResearchBudget.Pick(options, Known, ResearchUsage.Empty, new Dictionary<string, DateTimeOffset>(), Now, skip: new HashSet<string> { "agy:gemini-flash" });
        Assert.Null(none.Entry);
        Assert.Contains("exhausted", none.BlockedReason);
    }

    [Fact]
    public void Exhaustion_without_reset_lasts_until_next_wib_midnight()
    {
        var utcEvening = new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.Zero); // 01:00 WIB on the 23rd
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(7)), ResearchBudget.ExhaustedUntil(utcEvening, null));
        Assert.Equal(Now.AddHours(65), ResearchBudget.ExhaustedUntil(Now, Now.AddHours(65)));
    }

    [Fact]
    public void Dedupe_rules()
    {
        var o = new ResearchOptions();
        Assert.Equal(DedupeDecision.Duplicate, ResearchBudget.Dedupe("stock", true, hasOpenJob: true, null, Now, o));
        Assert.Equal(DedupeDecision.Cached, ResearchBudget.Dedupe("stock", false, false, Now.AddHours(-5), Now, o));
        Assert.Equal(DedupeDecision.Queue, ResearchBudget.Dedupe("stock", false, false, Now.AddHours(-7), Now, o));
        Assert.Equal(DedupeDecision.Queue, ResearchBudget.Dedupe("stock", force: true, false, Now.AddMinutes(-5), Now, o));
        Assert.Equal(DedupeDecision.Queue, ResearchBudget.Dedupe("ask", false, false, Now.AddMinutes(-1), Now, o));
    }

    [Fact]
    public void Subjects_are_normalized()
    {
        Assert.Equal("BBCA", ResearchQueue.NormalizeSubject("stock", " bbca "));
        Assert.Equal("IHSG", ResearchQueue.NormalizeSubject("brief_close", "anything"));
        Assert.Equal(200, ResearchQueue.NormalizeSubject("ask", new string('q', 500)).Length);
    }
}

public class BatchPlannerTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 45, 0, TimeSpan.FromHours(7));

    static BatchContext Context() => new(
        Watchlist: ["BBCA", "ADRO", "TLKM"],
        Moves: new Dictionary<string, double> { ["BBCA"] = 0.5, ["ADRO"] = 4.2, ["TLKM"] = -1, ["GOTO"] = -7, ["BREN"] = 9, ["ASII"] = 0.1 },
        SectorMoves: new Dictionary<string, double> { ["Energy"] = 2.5, ["Financials"] = 0.4, ["Technology"] = -2.1, ["Industrials"] = 0.1, ["Consumer"] = -0.3 },
        LatestStockInsight: new Dictionary<string, DateTimeOffset> { ["BBCA"] = Now.AddHours(-2) },
        CoreCodes: new HashSet<string> { "BBCA", "ADRO", "TLKM", "GOTO", "BREN", "ASII" });

    [Fact]
    public void Close_batch_orders_by_priority_and_respects_cap()
    {
        var plan = BatchPlanner.Plan(BatchKind.Close, Context(), Now, batchCap: 8);

        Assert.Equal(8, plan.Count);
        Assert.Equal(ResearchKinds.BriefClose, plan[0].Kind);
        Assert.Equal(["ADRO", "TLKM"], plan.Where(p => p.Priority == ResearchPriority.Watchlist).Select(p => p.Subject)); // BBCA is fresh
        Assert.Equal(["Energy", "Financials", "Consumer", "Technology"], plan.Where(p => p.Kind == ResearchKinds.Sector).Select(p => p.Subject));
        Assert.Equal("BREN", plan[^1].Subject); // biggest mover fills the last slot; ADRO is not queued twice
        Assert.True(plan.Select(p => p.Priority).SequenceEqual(plan.Select(p => p.Priority).OrderByDescending(p => p)));
    }

    [Fact]
    public void Small_cap_keeps_the_brief_first()
    {
        var plan = BatchPlanner.Plan(BatchKind.Close, Context(), Now, batchCap: 2);
        Assert.Equal([ResearchKinds.BriefClose, ResearchKinds.Stock], plan.Select(p => p.Kind));
    }

    [Fact]
    public void Morning_midday_and_weekend_batches()
    {
        Assert.Equal([ResearchKinds.BriefOpen, ResearchKinds.Stock], BatchPlanner.Plan(BatchKind.Morning, Context(), Now, 8).Select(p => p.Kind));
        Assert.Equal(["ADRO"], BatchPlanner.Plan(BatchKind.Midday, Context(), Now, 8).Select(p => p.Subject));
        Assert.Equal(ResearchKinds.BriefWeek, Assert.Single(BatchPlanner.Plan(BatchKind.Weekend, Context(), Now, 8)).Kind);
    }
}

public class ScheduleTests
{
    static readonly MarketCalendar Calendar = MarketCalendar.Load(IdxRules.Load());
    static DateTimeOffset Wib(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Nothing_due_before_the_first_batch()
    {
        // Thu 22 Oct 07:00; the previous close batch ran.
        Assert.Null(ResearchSchedule.Due(Wib(2026, 10, 22, 7, 0), Calendar, false, new HashSet<string> { "close:2026-10-21" }));
    }

    [Fact]
    public void Due_batch_until_it_has_run()
    {
        var due = ResearchSchedule.Due(Wib(2026, 10, 22, 7, 46), Calendar, false, new HashSet<string>());
        Assert.Equal("morning:2026-10-22", due!.Id);
        Assert.Null(ResearchSchedule.Due(Wib(2026, 10, 22, 7, 50), Calendar, false, new HashSet<string> { due.Id }));
    }

    [Fact]
    public void Only_the_latest_missed_batch_runs()
    {
        // PC off since Thursday morning, back at 18:00: the morning brief is never back-filled, only the close.
        var due = ResearchSchedule.Due(Wib(2026, 10, 22, 18, 0), Calendar, false, new HashSet<string>());
        Assert.Equal("close:2026-10-22", due!.Id);
    }

    [Fact]
    public void Midday_only_when_enabled()
    {
        Assert.Equal("morning:2026-10-22", ResearchSchedule.Due(Wib(2026, 10, 22, 12, 30), Calendar, false, new HashSet<string>())!.Id);
        Assert.Equal("midday:2026-10-22", ResearchSchedule.Due(Wib(2026, 10, 22, 12, 30), Calendar, true, new HashSet<string>())!.Id);
    }

    [Fact]
    public void Holidays_skip_trading_batches_but_not_the_weekend_brief()
    {
        // Mon 17 Aug 2026 is a holiday: at 17:00 the latest occurrence is Sunday's 19:00 week brief, which is too old by then.
        Assert.Null(ResearchSchedule.Due(Wib(2026, 8, 17, 17, 0), Calendar, false, new HashSet<string>()));
        Assert.Equal("weekend:2026-08-16", ResearchSchedule.Due(Wib(2026, 8, 16, 19, 5), Calendar, false, new HashSet<string>())!.Id);
        Assert.DoesNotContain(ResearchSchedule.OccurrencesOn(new DateOnly(2026, 8, 17), Calendar, true), _ => true);
    }

    [Fact]
    public void Stale_missed_batches_are_dropped()
    {
        // Saturday noon: Friday's close (16:45) is more than 16h old, so nothing runs.
        Assert.Null(ResearchSchedule.Due(Wib(2026, 10, 24, 12, 0), Calendar, false, new HashSet<string>()));
        // Monday 07:00 after a weekend off: Sunday's week brief is still worth running.
        Assert.Equal("weekend:2026-10-25", ResearchSchedule.Due(Wib(2026, 10, 26, 7, 0), Calendar, false, new HashSet<string>())!.Id);
    }

    [Fact]
    public void Upcoming_lists_each_batch_once_in_time_order()
    {
        var next = ResearchSchedule.Upcoming(Wib(2026, 10, 23, 17, 0), Calendar, false); // Friday after the close
        Assert.Equal([BatchKind.Weekend, BatchKind.Morning, BatchKind.Close], next.Select(n => n.Kind));
        Assert.Equal(Wib(2026, 10, 25, 19, 0), next[0].At);
        Assert.Equal(Wib(2026, 10, 26, 7, 45), next[1].At);
    }

    [Theory]
    [InlineData(7, 45, DayOfWeek.Monday, 7, 7, 45, DayOfWeek.Monday)]
    [InlineData(7, 45, DayOfWeek.Monday, 0, 0, 45, DayOfWeek.Monday)]     // UTC
    [InlineData(7, 45, DayOfWeek.Monday, -5, 19, 45, DayOfWeek.Sunday)]   // UTC-5 crosses back a day
    [InlineData(19, 0, DayOfWeek.Sunday, 9, 21, 0, DayOfWeek.Sunday)]
    [InlineData(19, 0, DayOfWeek.Sunday, 13, 1, 0, DayOfWeek.Monday)]     // UTC+13 crosses forward a day
    public void Schedule_installer_converts_wib_to_local_time(int h, int m, DayOfWeek day, int offset, int eh, int em, DayOfWeek eday)
    {
        var (time, days) = ScheduleInstaller.ToLocal(new TimeOnly(h, m), [day], TimeSpan.FromHours(offset));
        Assert.Equal(new TimeOnly(eh, em), time);
        Assert.Equal([eday], days);
    }

    [Fact]
    public void Schedule_installer_builds_schtasks_arguments()
    {
        var task = ScheduleInstaller.Tasks.Single(t => t.Name == "SumireResearchClose");
        var args = ScheduleInstaller.CreateArgs(task, @"C:\apps\Sumire.exe", new TimeOnly(16, 45), task.Days);
        Assert.Equal(["/Create", "/F", "/TN", "SumireResearchClose", "/TR", "\"C:\\apps\\Sumire.exe\" research --once",
            "/SC", "WEEKLY", "/D", "MON,TUE,WED,THU,FRI", "/ST", "16:45", "/RL", "LIMITED"], args);
        Assert.Equal(["SumireResearchMorning", "SumireEod", "SumireResearchClose", "SumireResearchWeekend"], ScheduleInstaller.Tasks.Select(t => t.Name));
    }
}
