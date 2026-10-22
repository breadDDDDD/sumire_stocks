using Sumire.Api.Analytics.Core;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;

namespace Sumire.Api.Analytics.Services;

/// <summary>Everything computed for one instrument from its daily bars (once per trading day).</summary>
public sealed record StockAnalytics
{
    public required Instrument Instrument { get; init; }
    public required IReadOnlyList<Bar> Bars { get; init; }
    public required TechnicalsSnapshot Technicals { get; init; }
    public required RiskSnapshot Risk { get; init; }
    public GarchFit? Garch { get; init; }
    public BaselineForecast? Baseline { get; init; }
    public required SignalResult Score { get; init; }
    public Fundamental? Fundamental { get; init; }
    /// <summary>3- and 6-month return minus IHSG's, as fractions.</summary>
    public double? Rel3M { get; init; }
    public double? Rel6M { get; init; }
    /// <summary>Trading days since the last golden/death cross (0 = on the last bar).</summary>
    public int? GoldenCrossDays { get; init; }
    public int? DeathCrossDays { get; init; }

    public string Code => Instrument.Code;
    public DateOnly AsOf => Technicals.Date;
    public decimal Last => Technicals.Close;
}

public sealed record Breadth(
    DateOnly Date, int Total, int Advancers, int Decliners, int Unchanged,
    double PctAboveSma50, double PctAboveSma200, int NewHighs, int NewLows);

public sealed record Regime(string Label, int Score, IReadOnlyList<string> Reasons)
{
    public const string RiskOn = "Risk-on", Neutral = "Neutral", RiskOff = "Risk-off";
}

public sealed record SectorMember(string Code, string Name, decimal? MarketCap, double Weight, decimal Last, double? ChgPct);

/// <summary>Own sector index built from member stocks. Perf and relative returns are in percent.</summary>
public sealed record SectorIndex(
    string Name,
    string Weighting,
    IReadOnlyList<Bar> Levels,
    double? D1, double? W1, double? M1, double? M3,
    double? RsM1, double? RsM3,
    RotationPoint? Rotation,
    IReadOnlyList<RotationPoint> Trail,
    IReadOnlyList<SectorMember> Members);

public sealed record SectorMedians(double? Pe, double? Pb, double? Roe, double? DivYield, int N);

/// <summary>The per-trading-day analytics snapshot of the core universe.</summary>
public sealed record Universe
{
    /// <summary>The trading day this snapshot is for (latest completed session when it was built).</summary>
    public required DateOnly ExpectedDate { get; init; }
    /// <summary>Date of IHSG's last bar (what the numbers really reflect).</summary>
    public required DateOnly? AsOf { get; init; }
    public required DateTimeOffset BuiltAt { get; init; }
    /// <summary>"db" or "yahoo" (DB unavailable).</summary>
    public required string Source { get; init; }
    public required IReadOnlyDictionary<string, StockAnalytics> Stocks { get; init; }
    public StockAnalytics? Ihsg { get; init; }
    public StockAnalytics? Lq45 { get; init; }
    public Breadth? Breadth { get; init; }
    public required Regime Regime { get; init; }
    public required IReadOnlyList<SectorIndex> Sectors { get; init; }
    public required IReadOnlyDictionary<string, SectorMedians> SectorMedians { get; init; }
    public required IReadOnlyList<ForecastMetric> Metrics { get; init; }

    public StockAnalytics? Find(string code) => code switch
    {
        "IHSG" => Ihsg,
        "LQ45" => Lq45,
        _ => Stocks.GetValueOrDefault(code),
    };
}

/// <summary>Raw inputs for <see cref="UniverseBuilder"/>; every collection may be empty when its source is down.</summary>
public sealed record UniverseInput
{
    public required DateOnly ExpectedDate { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<Instrument> Stocks { get; init; }
    public required IReadOnlyDictionary<string, List<Bar>> Bars { get; init; }
    public Instrument? IhsgInstrument { get; init; }
    public Instrument? Lq45Instrument { get; init; }
    public IReadOnlyDictionary<string, Fundamental> Fundamentals { get; init; } = new Dictionary<string, Fundamental>();
    public IReadOnlyList<ForecastMetric> Metrics { get; init; } = [];
    /// <summary>Latest non-baseline model forecasts per code.</summary>
    public IReadOnlyDictionary<string, List<Forecast>> ModelForecasts { get; init; } = new Dictionary<string, List<Forecast>>();
    public IReadOnlyDictionary<string, List<NewsInputs>> News { get; init; } = new Dictionary<string, List<NewsInputs>>();
    public SignalWeights Weights { get; init; } = SignalWeights.Default;
}
