using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sumire.Api.Research;

public sealed record InsightDriver(string Claim, string Evidence, List<string> Linked, List<string> SourceIds);
public sealed record InsightConnection(string From, List<string> To, string Text);
public sealed record InsightCatalyst(string Date, string Event);
public sealed record InsightSource(string Id, string Title, string Url, string Publisher, string PublishedAt);

/// <summary>The structured result a runner returns, matching insight.schema.json.</summary>
public sealed record InsightDocument(
    string Summary,
    List<InsightDriver> Drivers,
    List<InsightConnection> Connections,
    List<InsightCatalyst> Catalysts,
    List<string> Risks,
    double Sentiment,
    string Confidence,
    string WhatWouldChangeThisView,
    List<InsightSource> Sources)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Parses and normalizes (null lists → empty, sentiment clamped, confidence lower-cased). Null when the shape is unusable.</summary>
    public static InsightDocument? TryParse(JsonNode? node, out string? error)
    {
        error = null;
        if (node is not JsonObject)
        {
            error = "result is not a JSON object";
            return null;
        }
        InsightDocument? doc;
        try { doc = node.Deserialize<InsightDocument>(Json); }
        catch (JsonException ex)
        {
            error = "result does not match the insight schema: " + ex.Message.Split('\n')[0];
            return null;
        }
        if (doc is null || string.IsNullOrWhiteSpace(doc.Summary))
        {
            error = "result has no summary";
            return null;
        }
        var confidence = (doc.Confidence ?? "").Trim().ToLowerInvariant() switch
        {
            "high" => "high",
            "med" or "medium" => "med",
            _ => "low",
        };
        // Models leave out fields or send nulls inside lists; the validator and the UI expect no nulls anywhere.
        static List<string> Strings(List<string>? list) => (list ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return doc with
        {
            Drivers = (doc.Drivers ?? []).OfType<InsightDriver>()
                .Select(d => d with { Claim = d.Claim ?? "", Evidence = d.Evidence ?? "", Linked = Strings(d.Linked), SourceIds = Strings(d.SourceIds) })
                .Where(d => d.Claim != "" || d.Evidence != "").ToList(),
            Connections = (doc.Connections ?? []).OfType<InsightConnection>()
                .Select(c => c with { From = c.From ?? "", To = Strings(c.To), Text = c.Text ?? "" }).ToList(),
            Catalysts = (doc.Catalysts ?? []).OfType<InsightCatalyst>()
                .Select(c => c with { Date = c.Date ?? "", Event = c.Event ?? "" }).Where(c => c.Event != "").ToList(),
            Risks = Strings(doc.Risks),
            Sources = (doc.Sources ?? []).OfType<InsightSource>()
                .Select(x => x with { Id = x.Id ?? "", Title = x.Title ?? "", Url = x.Url ?? "", Publisher = x.Publisher ?? "", PublishedAt = x.PublishedAt ?? "" })
                .ToList(),
            Sentiment = double.IsFinite(doc.Sentiment) ? Math.Clamp(doc.Sentiment, -1, 1) : 0,
            Confidence = confidence,
            WhatWouldChangeThisView = doc.WhatWouldChangeThisView ?? "",
        };
    }

    /// <summary>Linked tickers/factors across drivers and connections (stored in the insight body).</summary>
    public IReadOnlyList<string> LinkedTickers() =>
        Drivers.SelectMany(d => d.Linked).Concat(Connections.SelectMany(c => c.To.Append(c.From)))
            .Select(x => x.Trim().ToUpperInvariant()).Where(x => x != "").Distinct().ToList();

    /// <summary>jsonb body for the insights table (everything except sources).</summary>
    public string BodyJson() => JsonSerializer.Serialize(new
    {
        Summary, Drivers, Connections, Catalysts, Risks, WhatWouldChangeThisView, Linked = LinkedTickers(),
    }, Json);

    public string SourcesJson() => JsonSerializer.Serialize(Sources, Json);
}

/// <summary>insight.schema.json, copied next to the exe (Research/insight.schema.json).</summary>
public static class InsightSchema
{
    public const string FileName = "insight.schema.json";

    static readonly Lazy<string> Content = new(() => File.ReadAllText(PathOnDisk));

    public static string PathOnDisk
    {
        get
        {
            var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "Research", FileName) };
            if (Common.AppPaths.ProjectDir is { } project) candidates.Add(Path.Combine(project, "Research", FileName));
            return candidates.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException($"{FileName} not found next to the exe or in the project folder");
        }
    }

    public static string Text => Content.Value;

    /// <summary>Schema on one line, for CLIs that take it as an argument.</summary>
    public static string Minified => JsonNode.Parse(Text)!.ToJsonString();
}
