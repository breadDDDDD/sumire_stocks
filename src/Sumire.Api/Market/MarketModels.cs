namespace Sumire.Api.Market;

/// <summary>One OHLCV bar. Time is the bar start in the exchange's local offset (WIB for .JK).</summary>
public sealed record PriceBar(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close, decimal AdjClose, long Volume)
{
    public DateOnly Date => DateOnly.FromDateTime(Time.DateTime);
}

/// <summary>Dividend (Amount per share) or split (Amount = numerator / denominator, Ratio "2:1").</summary>
public sealed record CorporateAction(DateOnly Date, string Type, decimal Amount, string? Ratio = null);

public sealed record ChartData(
    string Symbol,
    string? Name,
    string? Currency,
    TimeSpan Offset,
    decimal? RegularMarketPrice,
    decimal? PreviousClose,
    DateTimeOffset? RegularMarketTime,
    IReadOnlyList<PriceBar> Bars,
    IReadOnlyList<CorporateAction> Events);

/// <summary>Raw Yahoo quote keyed by Yahoo symbol.</summary>
public sealed record YahooQuote(
    string Symbol,
    string? Name,
    decimal? Price,
    decimal? Change,
    decimal? ChangePct,
    decimal? PrevClose,
    decimal? Open,
    decimal? High,
    decimal? Low,
    long? Volume,
    DateTimeOffset? Time,
    string? MarketState,
    decimal? FiftyTwoWeekHigh,
    decimal? FiftyTwoWeekLow,
    decimal? MarketCap);

/// <summary>Quote as served by the API and pushed over SignalR. Time is WIB.</summary>
public sealed record Quote(
    string Code,
    string? Name,
    decimal? Price,
    decimal? Change,
    decimal? ChangePct,
    decimal? PrevClose,
    decimal? Open,
    decimal? High,
    decimal? Low,
    long? Volume,
    decimal? Value,
    DateTimeOffset? Time,
    // "live" (delayed intraday quote) or "close" (last daily close)
    string Source);

public sealed class YahooException(string code, string message, int? status = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? Status { get; } = status;
    public bool IsNotFound => Code == "Not Found" || Status == 404;
}
