using Sumire.Api.Common;

namespace Sumire.Api.Research;

/// <summary>Today's research usage: jobs finished per runner:model since WIB midnight.</summary>
public sealed record ResearchUsage(IReadOnlyDictionary<string, int> ByEntry)
{
    public int Total => ByEntry.Values.Sum();
    public int For(RunnerEntry e) => ByEntry.GetValueOrDefault(e.Key);
    public static readonly ResearchUsage Empty = new(new Dictionary<string, int>());
}

public sealed record RunnerPick(RunnerEntry? Entry, string? BlockedReason);

/// <summary>Caps, exhaustion and fallback order. Pure, so it is tested with a fake clock.</summary>
public static class ResearchBudget
{
    /// <summary>First runner:model in RESEARCH_RUNNERS order that is known, not exhausted and under its caps.</summary>
    public static RunnerPick Pick(
        ResearchOptions options,
        ISet<string> knownRunners,
        ResearchUsage usage,
        IReadOnlyDictionary<string, DateTimeOffset> exhaustedUntil,
        DateTimeOffset now,
        ISet<string>? skip = null)
    {
        if (usage.Total >= options.DailyCap)
            return new(null, $"daily cap reached ({usage.Total}/{options.DailyCap})");

        foreach (var entry in options.Runners)
        {
            if (skip?.Contains(entry.Key) == true) continue;
            if (!knownRunners.Contains(entry.Runner)) continue;
            if (exhaustedUntil.TryGetValue(entry.Key, out var until) && until > now) continue;
            if (CapFor(options, entry) is { } cap && UsageMatching(options.Runners, usage, cap) >= cap.Cap) continue;
            return new(entry, null);
        }
        return new(null, "all runners are exhausted or at their model caps");
    }

    public static ModelCap? CapFor(ResearchOptions options, RunnerEntry entry) => options.ModelCaps.FirstOrDefault(c => c.Matches(entry));

    /// <summary>A model cap is a shared pool: usage of every configured entry the pattern matches.</summary>
    static int UsageMatching(IEnumerable<RunnerEntry> entries, ResearchUsage usage, ModelCap cap)
    {
        var matched = entries.Where(cap.Matches).Select(e => e.Key).ToHashSet();
        return usage.ByEntry.Where(kv => matched.Contains(kv.Key)).Sum(kv => kv.Value);
    }

    /// <summary>When a quota error does not say when it resets: the next WIB midnight.</summary>
    public static DateTimeOffset ExhaustedUntil(DateTimeOffset now, DateTimeOffset? resetAt)
    {
        if (resetAt is { } r && r > now) return r;
        var wib = Wib.ToWib(now);
        return new DateTimeOffset(wib.Date.AddDays(1), wib.Offset);
    }

    /// <summary>Drops expired exhaustion entries.</summary>
    public static Dictionary<string, DateTimeOffset> Active(IReadOnlyDictionary<string, DateTimeOffset> exhausted, DateTimeOffset now) =>
        exhausted.Where(kv => kv.Value > now).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Should a new request be researched, or is an existing job/insight good enough?</summary>
    public static DedupeDecision Dedupe(string kind, bool force, bool hasOpenJob, DateTimeOffset? latestInsightAt, DateTimeOffset now, ResearchOptions options)
    {
        if (hasOpenJob) return DedupeDecision.Duplicate;
        if (force || latestInsightAt is null) return DedupeDecision.Queue;
        var window = CacheWindow(kind, options);
        return now - latestInsightAt.Value < window ? DedupeDecision.Cached : DedupeDecision.Queue;
    }

    /// <summary>Stock/sector insights: RESEARCH_STOCK_CACHE_HOURS (6h). Briefs: one per session (6h). Free-form questions are never cached.</summary>
    public static TimeSpan CacheWindow(string kind, ResearchOptions options) => kind switch
    {
        ResearchKinds.Ask => TimeSpan.Zero,
        _ => options.StockCacheWindow,
    };
}

public enum DedupeDecision { Queue, Duplicate, Cached }
