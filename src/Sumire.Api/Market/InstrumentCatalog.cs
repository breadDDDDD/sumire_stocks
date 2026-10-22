using System.Text;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;

namespace Sumire.Api.Market;

/// <summary>
/// In-memory instrument list for search and code/symbol lookups. Starts from Seed/instruments.csv so search
/// works without the database; <see cref="ImportSeedAsync"/> upserts the seed into the DB and merges
/// on-demand (non-core) instruments back in.
/// </summary>
public sealed class InstrumentCatalog
{
    volatile Dictionary<string, Instrument> _byCode;
    volatile Dictionary<string, Instrument> _bySymbol;

    public InstrumentCatalog(IEnumerable<Instrument> instruments)
    {
        (_byCode, _bySymbol) = Index(instruments);
    }

    public static InstrumentCatalog FromSeed() =>
        new(File.Exists(SeedPaths.File("instruments.csv")) ? InstrumentCsv.Parse(File.ReadAllText(SeedPaths.File("instruments.csv"))) : []);

    public IReadOnlyCollection<Instrument> All => _byCode.Values;

    public IEnumerable<Instrument> Core => _byCode.Values.Where(i => i.Core && i.Active);

    /// <summary>Indexes and macro symbols shown on the ticker tape.</summary>
    public IEnumerable<Instrument> Tape => _byCode.Values.Where(i => i.IsIndex && i.Active);

    public Instrument? Find(string code) => _byCode.GetValueOrDefault(code.Trim().ToUpperInvariant());

    public Instrument? FindBySymbol(string symbol) => _bySymbol.GetValueOrDefault(symbol);

    public void Add(Instrument instrument)
    {
        var all = _byCode.Values.Where(i => i.Code != instrument.Code).Append(instrument);
        (_byCode, _bySymbol) = Index(all);
    }

    public void Replace(IEnumerable<Instrument> instruments) => (_byCode, _bySymbol) = Index(instruments);

    /// <summary>Exact code, then code prefix, then name word prefix, then name contains; core names first within a rank.</summary>
    public IReadOnlyList<Instrument> Search(string? q, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        q = q?.Trim();
        if (string.IsNullOrEmpty(q))
            return _byCode.Values.Where(i => i.Core && !i.IsIndex).OrderBy(i => i.Code).Take(limit).ToList();

        var upper = q.ToUpperInvariant();
        return _byCode.Values
            .Select(i => (i, rank: Rank(i, upper)))
            .Where(x => x.rank < 4)
            .OrderBy(x => x.rank).ThenByDescending(x => x.i.Core).ThenBy(x => x.i.Code)
            .Take(limit)
            .Select(x => x.i)
            .ToList();
    }

    static int Rank(Instrument i, string q)
    {
        if (i.Code == q) return 0;
        if (i.Code.StartsWith(q, StringComparison.Ordinal)) return 1;
        var name = i.Name.ToUpperInvariant();
        if (name.StartsWith(q, StringComparison.Ordinal) || name.Contains(" " + q, StringComparison.Ordinal)) return 2;
        if (name.Contains(q, StringComparison.Ordinal)) return 3;
        return 4;
    }

    static (Dictionary<string, Instrument>, Dictionary<string, Instrument>) Index(IEnumerable<Instrument> instruments)
    {
        var byCode = new Dictionary<string, Instrument>(StringComparer.Ordinal);
        foreach (var i in instruments) byCode[i.Code] = i;
        var bySymbol = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in byCode.Values) bySymbol[i.YahooSymbol] = i;
        return (byCode, bySymbol);
    }

    /// <summary>Upserts seed instruments into the DB (idempotent) and reloads the catalog from the DB.</summary>
    public async Task<int> ImportSeedAsync(SumireDbContext db, CancellationToken ct)
    {
        var path = SeedPaths.File("instruments.csv");
        var seed = File.Exists(path) ? InstrumentCsv.Parse(await File.ReadAllTextAsync(path, ct)) : [];
        var existing = await db.Instruments.ToDictionaryAsync(i => i.Code, ct);
        var changed = 0;
        foreach (var s in seed)
        {
            if (!existing.TryGetValue(s.Code, out var row))
            {
                db.Instruments.Add(s);
                existing[s.Code] = s;
                changed++;
                continue;
            }
            if (row.YahooSymbol != s.YahooSymbol || row.Name != s.Name || row.Sector != s.Sector || row.Industry != s.Industry
                || row.Board != s.Board || row.IsIndex != s.IsIndex || row.GroupName != s.GroupName || row.Core != s.Core
                || !row.Active || !row.IndexMemberships.SequenceEqual(s.IndexMemberships))
            {
                row.YahooSymbol = s.YahooSymbol; row.Name = s.Name; row.Sector = s.Sector; row.Industry = s.Industry;
                row.Board = s.Board; row.IsIndex = s.IsIndex; row.GroupName = s.GroupName; row.Core = s.Core;
                row.IndexMemberships = s.IndexMemberships; row.Active = true;
                changed++;
            }
        }
        // Stocks dropped from the seed stay (history is kept) but are no longer core.
        var seedCodes = seed.Select(s => s.Code).ToHashSet();
        foreach (var row in existing.Values.Where(r => r.Core && !seedCodes.Contains(r.Code)))
        {
            row.Core = false;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        Replace(existing.Values);
        return changed;
    }
}

/// <summary>Parser for Seed/instruments.csv (header row, comma-separated, RFC 4180 quotes).</summary>
public static class InstrumentCsv
{
    public static List<Instrument> Parse(string text)
    {
        var rows = ReadRows(text).ToList();
        if (rows.Count == 0) return [];
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.IndexOf(name) is var i && i >= 0 ? i : throw new InvalidDataException($"instruments.csv: missing column '{name}'");
        int code = Col("code"), symbol = Col("yahoo_symbol"), name = Col("name"), sector = Col("sector"), industry = Col("industry"),
            board = Col("board"), isIndex = Col("is_index"), memberships = Col("index_memberships"), group = Col("group_name"), core = Col("core");

        var list = new List<Instrument>();
        foreach (var r in rows.Skip(1))
        {
            if (r.Count < header.Count || string.IsNullOrWhiteSpace(r[code])) continue;
            list.Add(new Instrument
            {
                Code = r[code].Trim().ToUpperInvariant(),
                YahooSymbol = r[symbol].Trim(),
                Name = r[name].Trim(),
                Sector = NullIfEmpty(r[sector]),
                Industry = NullIfEmpty(r[industry]),
                Board = NullIfEmpty(r[board]),
                IsIndex = Bool(r[isIndex]),
                IndexMemberships = r[memberships].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                GroupName = NullIfEmpty(r[group]),
                Core = Bool(r[core]),
                Active = true,
            });
        }
        return list;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static bool Bool(string s) => s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || s.Trim() == "1";

    static IEnumerable<List<string>> ReadRows(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) yield return row;
                row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
