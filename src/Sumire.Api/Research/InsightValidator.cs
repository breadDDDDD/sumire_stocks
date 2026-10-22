using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sumire.Api.Research;

public sealed record InsightIssue(string Code, string Message)
{
    public override string ToString() => Message;
}

/// <summary>
/// Local checks on a runner's insight (plan §6.3 step 5): every driver sourced, linked tickers exist, no stale sources,
/// and no price move or price for a packet ticker that contradicts the packet. Pure; the worker retries once on issues.
/// </summary>
public static partial class InsightValidator
{
    /// <summary>Factor codes allowed in linked[] besides IDX tickers.</summary>
    public static readonly HashSet<string> Factors = new(StringComparer.OrdinalIgnoreCase)
    {
        "IHSG", "COAL", "NICKEL", "CPO", "GOLD", "OIL", "WTI", "BRENT", "TIN", "COPPER", "GAS", "LNG", "RUBBER", "PULP",
        "USDIDR", "IDR", "USD", "BI_RATE", "FED", "FED_RATE", "US10Y", "CHINA", "INFLATION", "GDP", "MSCI", "FOREIGN_FLOW",
        "LQ45", "IDX30", "IDX80", "KOMPAS100", "SPX", "N225", "HSI", "STI", "SSE",
    };

    public static List<InsightIssue> Validate(
        InsightDocument doc, JsonNode? packet, ISet<string> knownTickers, DateTimeOffset now, int maxSourceAgeDays)
    {
        var issues = new List<InsightIssue>();
        var sourceIds = doc.Sources.Select(s => s.Id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (doc.Sources.Count == 0)
            issues.Add(new("no_sources", "The insight cites no sources."));

        for (var i = 0; i < doc.Drivers.Count; i++)
        {
            var d = doc.Drivers[i];
            if (d.SourceIds.Count == 0)
                issues.Add(new("driver_unsourced", $"Driver {i + 1} (\"{Short(d.Claim)}\") has no source id."));
            else if (d.SourceIds.FirstOrDefault(id => !sourceIds.Contains(id.Trim())) is { } missing)
                issues.Add(new("driver_unsourced", $"Driver {i + 1} cites source id \"{missing}\", which is not in sources[]."));
        }

        foreach (var code in doc.LinkedTickers())
            if (knownTickers.Count > 0 && !knownTickers.Contains(code) && !Factors.Contains(code) && TickerShape().IsMatch(code))
                issues.Add(new("unknown_ticker", $"Linked ticker {code} is not a known IDX instrument."));

        var today = DateOnly.FromDateTime(Common.Wib.ToWib(now).DateTime);
        foreach (var s in doc.Sources)
        {
            if (!TryParseDate(s.PublishedAt, out var published))
                issues.Add(new("source_date_missing", $"Source \"{Short(s.Title)}\" has no valid publishedAt date."));
            else if (today.DayNumber - published.DayNumber > maxSourceAgeDays)
                issues.Add(new("stale_source", $"Source \"{Short(s.Title)}\" is from {published:yyyy-MM-dd}, older than {maxSourceAgeDays} days."));
            if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                issues.Add(new("source_url_invalid", $"Source \"{Short(s.Title)}\" has no valid http(s) URL."));
        }

        var moves = Moves(packet);
        if (moves.Count > 0)
        {
            var texts = doc.Drivers.SelectMany(d => new[] { d.Claim, d.Evidence })
                .Concat(doc.Connections.Select(c => c.Text)).Append(doc.Summary);
            foreach (var issue in texts.SelectMany(t => CheckNumbers(t, moves)).DistinctBy(x => x.Message))
                issues.Add(issue);
        }
        return issues;
    }

    /// <summary>packet.moves: { "ADRO": { "last": 2450, "chg1d": 3.1, "chg5d": ... , "high52": ..., "low52": ... } }.</summary>
    public static Dictionary<string, Dictionary<string, double>> Moves(JsonNode? packet)
    {
        var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        if (packet?["moves"] is not JsonObject moves) return result;
        foreach (var (code, node) in moves)
        {
            if (node is not JsonObject fields) continue;
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, v) in fields)
                if (v is JsonValue jv && jv.TryGetValue<double>(out var d)) values[name] = d;
            result[code] = values;
        }
        return result;
    }

    static IEnumerable<InsightIssue> CheckNumbers(string text, Dictionary<string, Dictionary<string, double>> moves)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var sentence in SentenceSplit().Split(text))
        {
            if (LongHorizon().IsMatch(sentence)) continue;
            var mentioned = moves.Keys.Where(code => Regex.IsMatch(sentence, $@"\b{Regex.Escape(code)}\b")).ToList();
            if (mentioned.Count != 1) continue; // ambiguous which ticker a number belongs to
            var code = mentioned[0];
            var values = moves[code];

            foreach (Match m in PctMove(code).Matches(sentence))
            {
                if (!m.Groups["verb"].Success && !m.Groups["sign"].Success) continue; // a share or weight, not a move
                if (!TryParseNumber(m.Groups["n"].Value, out var pct)) continue;
                var sign = (m.Groups["verb"].Success && DownWord().IsMatch(m.Groups["verb"].Value)) || m.Groups["sign"].Value is "-" or "−" ? -1 : 1;
                var claimed = sign * Math.Abs(pct);
                var changes = values.Where(kv => kv.Key.StartsWith("chg", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
                if (changes.Count == 0) continue;
                if (!changes.Any(v => Math.Abs(v - claimed) <= Math.Max(0.6, Math.Abs(v) * 0.2)))
                    yield return new("number_mismatch",
                        $"\"{Short(m.Value)}\" contradicts the packet: {code} moved {string.Join(", ", values.Where(kv => kv.Key.StartsWith("chg")).Select(kv => $"{kv.Key} {kv.Value:+0.0;-0.0}%"))}.");
            }

            if (!values.TryGetValue("last", out var last) || last <= 0) continue;
            foreach (Match m in PriceMention(code).Matches(sentence))
            {
                if (!TryParseNumber(m.Groups["n"].Value, out var price, price: true) || price <= 0) continue;
                var low = Math.Min(last * 0.75, values.GetValueOrDefault("low52", last) * 0.9);
                var high = Math.Max(last * 1.25, values.GetValueOrDefault("high52", last) * 1.1);
                if (price < low || price > high)
                    yield return new("number_mismatch", $"\"{Short(m.Value)}\" contradicts the packet: {code} last price is {last:0.##}.");
            }
        }
    }

    /// <summary>Parses 3.1, 3,1, 9,875, 1.234,5 and, for prices (Rp 9.875 is Indonesian thousands), 9.875.</summary>
    public static bool TryParseNumber(string raw, out double value, bool price = false)
    {
        var s = raw.Trim();
        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
            s = lastComma > lastDot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (lastComma >= 0)
            s = s.Length - lastComma - 1 == 3 ? s.Replace(",", "") : s.Replace(',', '.');
        else if (lastDot >= 0 && s.Length - lastDot - 1 == 3 && (price || s.Count(c => c == '.') > 1))
            s = s.Replace(".", "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (DateOnly.TryParseExact(text.Trim(), ["yyyy-MM-dd", "yyyy-MM"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            date = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }
        return false;
    }

    static string Short(string text) => text.Length > 60 ? text[..60] + "…" : text;

    static Regex PctMove(string code) => new(
        $@"\b{Regex.Escape(code)}\b[^.%]{{0,40}}?(?:(?<verb>\b(?:{UpWords}|{DownWords})\b)\s*(?:by\s+|sebesar\s+)?)?(?<sign>[+\-−])?\s*(?<n>\d+(?:[.,]\d+)?)\s?%",
        RegexOptions.IgnoreCase);

    static Regex PriceMention(string code) => new(
        $@"\b{Regex.Escape(code)}\b[^.]{{0,40}}?Rp\s?(?<n>\d{{1,3}}(?:[.,]\d{{3}})+|\d+(?:[.,]\d+)?)(?!\s*(?:[.,]?\d)|\s*(?:T\b|B\b|M\b|tn|bn|tril|trillion|triliun|miliar|billion|juta|million|ribu))",
        RegexOptions.IgnoreCase);

    const string UpWords = @"up|rose|rise|rises|risen|gain(?:ed|s)?|jump(?:ed|s)?|climb(?:ed|s)?|rall(?:ied|ies)|surge(?:d|s)?|advance(?:d|s)?|naik|menguat|melonjak";
    const string DownWords = @"down|fell|fall|falls|drop(?:ped|s)?|slid|slip(?:ped|s)?|decline(?:d|s)?|los(?:t|es)|sank|plunge(?:d|s)?|tumble(?:d|s)?|turun|melemah|anjlok|merosot";

    [GeneratedRegex(@"^(?:" + DownWords + ")$", RegexOptions.IgnoreCase)]
    private static partial Regex DownWord();

    [GeneratedRegex(@"(?<=[.!?;])\s+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\b(ytd|year|years|yoy|tahun|quarter|kuartal|qoq|since|sejak|annual|52-week)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LongHorizon();

    [GeneratedRegex(@"^[A-Z]{4}$")]
    private static partial Regex TickerShape();
}
