using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Sumire.Api.Data;

namespace Sumire.Api.Market.Rss;

/// <summary>A headline from a free RSS feed. Title and summary are untrusted third-party text.</summary>
public sealed record Headline(string Title, string Url, string Publisher, DateTimeOffset? PublishedAt, string? Summary, IReadOnlyList<string> Tickers);

public static partial class RssParser
{
    const int SummaryMax = 300;

    /// <summary>Parses RSS 2.0 items. <paramref name="linkContains"/> keeps only items whose link contains it (for site-wide feeds).</summary>
    public static List<Headline> Parse(string xml, string publisher, string? linkContains = null)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);

        var list = new List<Headline>();
        foreach (var item in doc.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var title = Clean(Child(item, "title"));
            var link = (Child(item, "link") ?? Child(item, "guid"))?.Trim();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link) || !link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (linkContains is not null && !link.Contains(linkContains, StringComparison.OrdinalIgnoreCase)) continue;

            var summary = Clean(Child(item, "description"));
            if (summary is { Length: > SummaryMax }) summary = summary[..SummaryMax].TrimEnd() + "…";
            list.Add(new Headline(title, link, publisher, ParseDate(Child(item, "pubDate")), string.IsNullOrEmpty(summary) ? null : summary, []));
        }
        return list;
    }

    static string? Child(XElement item, string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>Strips tags, decodes entities and collapses whitespace.</summary>
    public static string? Clean(string? text)
    {
        if (text is null) return null;
        text = WebUtility.HtmlDecode(TagRegex().Replace(text, " "));
        text = TagRegex().Replace(text, " "); // descriptions sometimes hold encoded <img> tags
        return SpaceRegex().Replace(text, " ").Trim();
    }

    static readonly string[] DateFormats = ["ddd, dd MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm:ss zzz", "dd MMM yyyy HH:mm:ss zzz", "yyyy-MM-ddTHH:mm:sszzz"];

    public static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        // RFC 822 offsets come as +0700; .NET's zzz wants +07:00.
        var m = OffsetRegex().Match(s);
        if (m.Success) s = s[..m.Index] + m.Groups[1].Value + m.Groups[2].Value + ":" + m.Groups[3].Value;
        s = s.Replace(" GMT", " +00:00").Replace(" WIB", " +07:00");
        if (DateTimeOffset.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return d;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d) ? d : null;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@" ([+-])(\d{2})(\d{2})$")]
    private static partial Regex OffsetRegex();
}

/// <summary>Links headlines to stocks: an IDX code in capitals as a whole word, or the company's short name.</summary>
public sealed partial class TickerMatcher
{
    readonly HashSet<string> _codes;
    readonly List<(Regex Name, string Code)> _names;

    public TickerMatcher(IEnumerable<Instrument> instruments)
    {
        var stocks = instruments.Where(i => !i.IsIndex).ToList();
        _codes = stocks.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);
        _names = stocks
            .Select(i => (Short: ShortName(i.Name), i.Code))
            .Where(x => x.Short.Length >= 8)
            .Select(x => (new Regex($@"\b{Regex.Escape(x.Short)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), x.Code))
            .ToList();
    }

    public IReadOnlyList<string> Match(string text)
    {
        var found = new List<string>();
        foreach (Match m in CodeRegex().Matches(text))
            if (_codes.Contains(m.Value) && !found.Contains(m.Value)) found.Add(m.Value);
        foreach (var (name, code) in _names)
            if (!found.Contains(code) && name.IsMatch(text)) found.Add(code);
        return found;
    }

    public Headline Tag(Headline h) => h with { Tickers = Match(h.Title + " " + h.Summary) };

    /// <summary>"PT Bank Central Asia Tbk" -> "Bank Central Asia"; "(Persero)" and "Tbk" are dropped.</summary>
    public static string ShortName(string name)
    {
        var s = name.Replace("(Persero)", "", StringComparison.OrdinalIgnoreCase);
        s = Regex.Replace(s, @"^\s*PT\.?\s+", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+Tbk\.?\s*$", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"\b[A-Z]{4}\b")]
    private static partial Regex CodeRegex();
}
