namespace Sumire.Api.Data;

// Tables live in schema "sumire"; names are snake_case via UseSnakeCaseNamingConvention.
// jsonb columns are plain strings holding JSON. Write DateTimeOffset values in UTC (Npgsql timestamptz rule).

public class Instrument
{
    public required string Code { get; set; }
    public required string YahooSymbol { get; set; }
    public required string Name { get; set; }
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public string? Board { get; set; }
    public bool IsIndex { get; set; }
    public string[] IndexMemberships { get; set; } = [];
    public string? GroupName { get; set; }
    public bool Core { get; set; }
    public bool Active { get; set; } = true;
}

public class DailyBar
{
    public required string Code { get; set; }
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal AdjClose { get; set; }
    public long Volume { get; set; }
}

public class Fundamental
{
    public required string Code { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public decimal? MarketCap { get; set; }
    public double? Pe { get; set; }
    public double? Pb { get; set; }
    public double? Roe { get; set; }
    public double? Der { get; set; }
    public double? Npm { get; set; }
    public decimal? Eps { get; set; }
    public double? DivYield { get; set; }
    public double? RevenueGrowth { get; set; }
    public double? EarningsGrowth { get; set; }
}

public class Forecast
{
    public required string Code { get; set; }
    public DateOnly AsOf { get; set; }
    /// <summary>Trading days ahead: 5, 20 or 60.</summary>
    public int Horizon { get; set; }
    /// <summary>e.g. "baseline", "chronos-2", "chronos-bolt-small".</summary>
    public required string Model { get; set; }
    public decimal P10 { get; set; }
    public decimal P50 { get; set; }
    public decimal P90 { get; set; }
    public double ProbUp { get; set; }
    public double Sigma { get; set; }
}

public class ForecastMetric
{
    public required string Model { get; set; }
    public int Horizon { get; set; }
    /// <summary>"all", "IHSG", a sector name or "news" for sentiment tracking.</summary>
    public required string Scope { get; set; }
    public double Mape { get; set; }
    public double DirAccuracy { get; set; }
    public double Coverage80 { get; set; }
    public int N { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SignalScore
{
    public required string Code { get; set; }
    public DateOnly AsOf { get; set; }
    /// <summary>-100 … +100.</summary>
    public double Total { get; set; }
    public double? Trend { get; set; }
    public double? Momentum { get; set; }
    public double? Volume { get; set; }
    public double? Volatility { get; set; }
    public double? Valuation { get; set; }
    public double? Forecast { get; set; }
    public double? News { get; set; }
    /// <summary>jsonb: { "trend": "Above MA50/200, ADX 28", ... } one-line reason per component.</summary>
    public string? Reasons { get; set; }
}

public class ResearchJob
{
    public long Id { get; set; }
    /// <summary>stock / brief_open / brief_close / brief_week / sector / ask</summary>
    public required string Kind { get; set; }
    public required string Subject { get; set; }
    public int Priority { get; set; }
    /// <summary>jsonb fact packet; cleared once the job is done.</summary>
    public string? Packet { get; set; }
    /// <summary>pending / running / done / failed / skipped</summary>
    public string Status { get; set; } = "pending";
    public string? Runner { get; set; }
    public string? Model { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? InsightId { get; set; }
}

public class Insight
{
    public long Id { get; set; }
    public required string Kind { get; set; }
    public required string Subject { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Runner { get; set; }
    public string? Model { get; set; }
    /// <summary>-1 … 1</summary>
    public double Sentiment { get; set; }
    /// <summary>low / med / high</summary>
    public required string Confidence { get; set; }
    /// <summary>jsonb: summary, drivers, connections, catalysts, risks, linked tickers.</summary>
    public required string Body { get; set; }
    /// <summary>jsonb array: title, url, publisher, date.</summary>
    public required string Sources { get; set; }
    /// <summary>jsonb array of validator warnings; null when the insight passed every check.</summary>
    public string? Warnings { get; set; }
}

public class Watchlist
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Sort { get; set; }
    public List<WatchlistItem> Items { get; set; } = [];
}

public class WatchlistItem
{
    public int WatchlistId { get; set; }
    public required string Code { get; set; }
    public int Sort { get; set; }
}

public class Alert
{
    public long Id { get; set; }
    /// <summary>Null for alerts that are not tied to one ticker (e.g. negative news on any watchlist stock).</summary>
    public string? Code { get; set; }
    /// <summary>price / pct / ma_cross / rsi / high52 / news</summary>
    public required string Type { get; set; }
    /// <summary>jsonb parameters for the alert type.</summary>
    public string Params { get; set; } = "{}";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
}

public class Setting
{
    public required string Key { get; set; }
    /// <summary>jsonb value.</summary>
    public required string Value { get; set; }
}
