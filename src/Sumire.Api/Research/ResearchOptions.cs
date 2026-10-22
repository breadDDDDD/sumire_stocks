using System.Text.RegularExpressions;

namespace Sumire.Api.Research;

/// <summary>One entry of RESEARCH_RUNNERS, e.g. agy:gemini-3.8-flash-medium.</summary>
public sealed record RunnerEntry(string Runner, string Model)
{
    public string Key => $"{Runner}:{Model}";
    public override string ToString() => Key;
}

/// <summary>Per-model daily cap from RESEARCH_MODEL_CAPS, e.g. claude-*:6 (glob on the model, or on runner:model).</summary>
public sealed record ModelCap(string Pattern, int Cap)
{
    public bool Matches(RunnerEntry entry) => Glob(Pattern).IsMatch(Pattern.Contains(':') ? entry.Key : entry.Model);

    static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);
}

/// <summary>Research settings from the environment (.env). Pause state and exhaustion live in the settings table.</summary>
public sealed class ResearchOptions
{
    public static readonly RunnerEntry[] DefaultRunners = [new("agy", "gemini-3.8-flash-medium"), new("agy", "claude-sonnet-4-6"), new("claude-code", "sonnet")];

    public IReadOnlyList<RunnerEntry> Runners { get; init; } = DefaultRunners;
    public bool Enabled { get; init; } = true;
    public int DailyCap { get; init; } = 20;
    public int BatchCap { get; init; } = 8;
    public IReadOnlyList<ModelCap> ModelCaps { get; init; } = [];
    public bool MiddayBatch { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(4);
    /// <summary>Sources older than this are flagged by the validator (briefs use a tighter window, see InsightValidator).</summary>
    public int SourceMaxAgeDays { get; init; } = 14;
    /// <summary>Stock insights younger than this are reused unless forced.</summary>
    public TimeSpan StockCacheWindow { get; init; } = TimeSpan.FromHours(6);
    /// <summary>Folder the headless agent runs in; null = research-workspace/ at the repo root, or %LOCALAPPDATA%/Sumire/research-workspace.</summary>
    public string? Workspace { get; init; }

    public static ResearchOptions FromConfig(IConfiguration config)
    {
        string? Get(string key) => config[key] is { Length: > 0 } v ? v.Trim() : null;
        int Int(string key, int fallback) => int.TryParse(Get(key), out var v) && v >= 0 ? v : fallback;
        bool Bool(string key, bool fallback) => bool.TryParse(Get(key), out var v) ? v : fallback;

        return new ResearchOptions
        {
            Runners = Get("RESEARCH_RUNNERS") is { } r && ParseRunners(r) is { Count: > 0 } list ? list : DefaultRunners,
            Enabled = Bool("RESEARCH_ENABLED", true),
            DailyCap = Int("RESEARCH_DAILY_CAP", 20),
            BatchCap = Int("RESEARCH_BATCH_CAP", 8),
            ModelCaps = ParseModelCaps(Get("RESEARCH_MODEL_CAPS") ?? ""),
            MiddayBatch = Bool("RESEARCH_MIDDAY", false),
            Timeout = TimeSpan.FromSeconds(Int("RESEARCH_TIMEOUT_SECONDS", 240)),
            SourceMaxAgeDays = Int("RESEARCH_SOURCE_MAX_AGE_DAYS", 14),
            StockCacheWindow = TimeSpan.FromHours(Int("RESEARCH_STOCK_CACHE_HOURS", 6)),
            Workspace = Get("RESEARCH_WORKSPACE"),
        };
    }

    /// <summary>"agy:gemini-3.8-flash-medium, claude-code:sonnet". Unknown runners are kept (the worker skips them with an error).</summary>
    public static List<RunnerEntry> ParseRunners(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(p => p[0] != "")
            .Select(p => new RunnerEntry(p[0].ToLowerInvariant(), p.Length > 1 ? p[1] : ""))
            .Distinct()
            .ToList();

    /// <summary>"claude-*:6, gemini-3.8-flash-*:15". The last colon separates the cap.</summary>
    public static List<ModelCap> ParseModelCaps(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => (part, colon: part.LastIndexOf(':')))
            .Where(x => x.colon > 0 && int.TryParse(x.part[(x.colon + 1)..], out _))
            .Select(x => new ModelCap(x.part[..x.colon].Trim(), int.Parse(x.part[(x.colon + 1)..])))
            .ToList();

    public string ResolveWorkspace()
    {
        if (Workspace is not null) return Path.GetFullPath(Workspace);
        var repo = Common.AppPaths.FindUpward(AppContext.BaseDirectory, d => File.Exists(Path.Combine(d, "SumirePlatform.sln")));
        return repo is not null
            ? Path.Combine(repo, "research-workspace")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sumire", "research-workspace");
    }
}
