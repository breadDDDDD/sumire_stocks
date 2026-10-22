using System.Text.Json;
using System.Text.Json.Serialization;
using Sumire.Api.Common;

namespace Sumire.Api.Market;

/// <summary>IDX trading rules from Seed/idx-rules.json: tick sizes, ARA/ARB limits, lot size, fees, sessions.</summary>
public sealed class IdxRules
{
    public const int Lot = 100;

    readonly TimeProvider _clock;

    public IdxRulesFile File { get; }

    public IdxRules(IdxRulesFile file, TimeProvider? clock = null)
    {
        File = file;
        _clock = clock ?? TimeProvider.System;
    }

    public static IdxRules Load(string? path = null, TimeProvider? clock = null)
    {
        path ??= SeedPaths.File("idx-rules.json");
        var file = JsonSerializer.Deserialize<IdxRulesFile>(System.IO.File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Empty rules file: {path}");
        return new IdxRules(file, clock);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public int LotSize => File.LotSize;
    public decimal MinPrice => File.MinPrice;
    public decimal BuyFee => File.Fees.Buy;
    public decimal SellFee => File.Fees.Sell;

    /// <summary>Value of one lot in Rupiah.</summary>
    public decimal LotValue(decimal price) => price * File.LotSize;

    public decimal TickSize(decimal price)
    {
        foreach (var band in File.TickSizes.Bands)
            if (band.Below is null || price < band.Below) return band.Tick;
        return File.TickSizes.Bands[^1].Tick;
    }

    public decimal RoundToTick(decimal price) => Round(price, MidpointRounding.AwayFromZero);
    public decimal RoundDownToTick(decimal price) => Round(price, MidpointRounding.ToZero);
    public decimal RoundUpToTick(decimal price) => Round(price, MidpointRounding.ToPositiveInfinity);

    decimal Round(decimal price, MidpointRounding mode)
    {
        if (price <= 0) return 0;
        var tick = TickSize(price);
        var units = price / tick;
        units = mode switch
        {
            MidpointRounding.ToZero => Math.Floor(units),
            MidpointRounding.ToPositiveInfinity => Math.Ceiling(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero),
        };
        return units * tick;
    }

    /// <summary>Auto-rejection limits for the next session from the previous close (rules in force on <paramref name="date"/>, default today WIB).</summary>
    public (decimal Upper, decimal Lower) AraArb(decimal prevClose, DateOnly? date = null)
    {
        var regime = RegimeFor(date ?? Wib.Today(_clock));
        var band = regime.Bands.FirstOrDefault(b => b.UpTo is null || prevClose <= b.UpTo) ?? regime.Bands[^1];

        var upper = band.AraAbs is { } araAbs ? prevClose + araAbs : RoundDownToTick(prevClose * (1 + (band.AraPct ?? 0)));
        var lower = band.ArbAbs is { } arbAbs ? prevClose - arbAbs : RoundUpToTick(prevClose * (1 - (band.ArbPct ?? 0)));
        return (upper, Math.Max(lower, File.MinPrice));
    }

    public AutoRejectionRegime RegimeFor(DateOnly date)
    {
        var match = File.AutoRejection.LastOrDefault(r => r.From <= date && (r.To is null || date <= r.To));
        return match ?? (date < File.AutoRejection[0].From ? File.AutoRejection[0] : File.AutoRejection[^1]);
    }

    /// <summary>Total cost of a buy including fees.</summary>
    public decimal BuyCost(decimal price, int lots) => price * lots * File.LotSize * (1 + File.Fees.Buy);

    /// <summary>Net proceeds of a sell after fees.</summary>
    public decimal SellProceeds(decimal price, int lots) => price * lots * File.LotSize * (1 - File.Fees.Sell);
}

public sealed record IdxRulesFile
{
    public int LotSize { get; init; } = IdxRules.Lot;
    public decimal MinPrice { get; init; } = 1;
    public FeeRates Fees { get; init; } = new();
    public required TickSizeTable TickSizes { get; init; }
    public required List<AutoRejectionRegime> AutoRejection { get; init; }
    public required SessionTable Sessions { get; init; }
}

public sealed record FeeRates
{
    public decimal Buy { get; init; } = 0.0015m;
    public decimal Sell { get; init; } = 0.0025m;
}

public sealed record TickSizeTable
{
    public bool Verified { get; init; }
    public required List<TickBand> Bands { get; init; }
}

/// <summary>Applies while price &lt; Below (null = no upper limit).</summary>
public sealed record TickBand(decimal? Below, decimal Tick);

public sealed record AutoRejectionRegime
{
    public DateOnly From { get; init; }
    public DateOnly? To { get; init; }
    public bool Verified { get; init; }
    public string? Note { get; init; }
    public required List<AraArbBand> Bands { get; init; }
}

/// <summary>Applies while previous close &lt;= UpTo (null = no upper limit). Either percentages or absolute Rupiah steps.</summary>
public sealed record AraArbBand
{
    public decimal? UpTo { get; init; }
    public decimal? AraPct { get; init; }
    public decimal? ArbPct { get; init; }
    public decimal? AraAbs { get; init; }
    public decimal? ArbAbs { get; init; }
}

public sealed record SessionTable
{
    public bool Verified { get; init; }
    public required DaySessions MonThu { get; init; }
    public required DaySessions Fri { get; init; }
}

/// <summary>Each entry is ["HH:mm", "HH:mm"] as [start, end) in WIB.</summary>
public sealed record DaySessions
{
    public required string[] PreOpen { get; init; }
    public required string[] Session1 { get; init; }
    public required string[] Session2 { get; init; }
    public required string[] PreClose { get; init; }

    public static TimeOnly Hm(string s) => TimeOnly.ParseExact(s, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [JsonIgnore] public TimeOnly OpenTime => DaySessions.Hm(PreOpen[0]);
    [JsonIgnore] public TimeOnly CloseTime => DaySessions.Hm(PreClose[1]);
}
