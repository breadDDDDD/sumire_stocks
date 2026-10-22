namespace Sumire.Api.Research;

public static class ResearchKinds
{
    public const string Stock = "stock";
    public const string BriefOpen = "brief_open";
    public const string BriefClose = "brief_close";
    public const string BriefWeek = "brief_week";
    public const string Sector = "sector";
    public const string Ask = "ask";
    /// <summary>Lock row marking a scheduled batch as run (subject = "close:2026-10-22"); never researched.</summary>
    public const string Batch = "batch";

    public static readonly string[] All = [Stock, BriefOpen, BriefClose, BriefWeek, Sector, Ask];

    public static bool IsValid(string kind) => All.Contains(kind);
    public static bool IsBrief(string kind) => kind is BriefOpen or BriefClose or BriefWeek;

    /// <summary>"open" | "close" | "week" → brief kind.</summary>
    public static string? BriefKind(string? shortName) => shortName?.ToLowerInvariant() switch
    {
        "open" or "morning" => BriefOpen,
        "close" or "recap" => BriefClose,
        "week" or "weekend" => BriefWeek,
        _ => null,
    };
}

/// <summary>Queue priority: brief &gt; on-command &gt; watchlist &gt; sectors &gt; movers (higher runs first).</summary>
public static class ResearchPriority
{
    public const int Brief = 100;
    public const int OnCommand = 80;
    public const int Watchlist = 60;
    public const int Sector = 40;
    public const int Mover = 20;
}
