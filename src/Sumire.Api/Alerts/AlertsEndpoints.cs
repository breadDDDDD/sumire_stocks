using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Market;
using Sumire.Api.Market.Rss;

namespace Sumire.Api.Alerts;

public static partial class AlertsEndpoints
{
    public sealed record WatchlistDto(int Id, string Name, int Sort, IReadOnlyList<WatchlistItemDto> Items);
    public sealed record WatchlistItemDto(string Code, int Sort);
    public sealed record NameBody(string? Name);
    public sealed record CodeBody(string? Code);
    public sealed record CodesBody(string[]? Codes);

    public sealed record AlertDto(long Id, string? Code, string Type, JsonElement Params, bool Active, DateTimeOffset CreatedAt, DateTimeOffset? LastTriggeredAt);
    public sealed record AlertBody(string? Code, string? Type, JsonElement? Params, bool? Active);

    const string DefaultWatchlist = "Main";
    const int MaxSettingBytes = 64 * 1024;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex SettingKeyRegex();

    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(DbUnavailableFilter);
        MapWatchlists(api);
        MapSettings(api);
        MapAlertRoutes(api);

        app.MapGet("/api/news", async (RssNewsClient news, string? code, int? limit, CancellationToken ct) =>
            await news.GetHeadlinesAsync(code, limit ?? 30, ct));
        return app;
    }

    // ---------- watchlists ----------

    static void MapWatchlists(RouteGroupBuilder api)
    {
        api.MapGet("/watchlists", async (SumireDbContext db, CancellationToken ct) =>
        {
            if (!await db.Watchlists.AnyAsync(ct))
            {
                db.Watchlists.Add(new Watchlist { Name = DefaultWatchlist });
                await db.SaveChangesAsync(ct);
            }
            var lists = await db.Watchlists.AsNoTracking().Include(w => w.Items).OrderBy(w => w.Sort).ThenBy(w => w.Id).ToListAsync(ct);
            return lists.Select(ToDto);
        });

        api.MapPost("/watchlists", async (SumireDbContext db, NameBody body, CancellationToken ct) =>
        {
            var name = body.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40) return Bad("name is required (max 40 characters).");
            var sort = await db.Watchlists.Select(w => (int?)w.Sort).MaxAsync(ct) ?? 0;
            var list = new Watchlist { Name = name, Sort = sort + 1 };
            db.Watchlists.Add(list);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/watchlists/{list.Id}", ToDto(list));
        });

        api.MapPut("/watchlists/{id:int}", async (SumireDbContext db, int id, NameBody body, CancellationToken ct) =>
        {
            var name = body.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40) return Bad("name is required (max 40 characters).");
            var list = await db.Watchlists.Include(w => w.Items).FirstOrDefaultAsync(w => w.Id == id, ct);
            if (list is null) return NotFound("Watchlist not found");
            list.Name = name;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(list));
        });

        api.MapDelete("/watchlists/{id:int}", async (SumireDbContext db, int id, CancellationToken ct) =>
            await db.Watchlists.Where(w => w.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : NotFound("Watchlist not found"));

        api.MapPost("/watchlists/{id:int}/items", async (SumireDbContext db, InstrumentCatalog catalog, int id, CodeBody body, CancellationToken ct) =>
        {
            var code = body.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code) || (catalog.Find(code) is null && !MarketDataService.IsIdxCode(code))) return Bad("Unknown code.");
            var list = await db.Watchlists.Include(w => w.Items).FirstOrDefaultAsync(w => w.Id == id, ct);
            if (list is null) return NotFound("Watchlist not found");
            if (list.Items.All(i => i.Code != code))
            {
                list.Items.Add(new WatchlistItem { Code = code, Sort = list.Items.Count == 0 ? 0 : list.Items.Max(i => i.Sort) + 1 });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(ToDto(list));
        });

        api.MapDelete("/watchlists/{id:int}/items/{code}", async (SumireDbContext db, int id, string code, CancellationToken ct) =>
        {
            code = code.Trim().ToUpperInvariant();
            return await db.WatchlistItems.Where(i => i.WatchlistId == id && i.Code == code).ExecuteDeleteAsync(ct) > 0
                ? Results.NoContent() : NotFound("Item not found");
        });

        api.MapPut("/watchlists/{id:int}/items", async (SumireDbContext db, int id, CodesBody body, CancellationToken ct) =>
        {
            var list = await db.Watchlists.Include(w => w.Items).FirstOrDefaultAsync(w => w.Id == id, ct);
            if (list is null) return NotFound("Watchlist not found");
            Reorder(list.Items, body.Codes ?? []);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(list));
        });
    }

    /// <summary>Codes in the given order first, then any others in their old order.</summary>
    public static void Reorder(List<WatchlistItem> items, IEnumerable<string> codes)
    {
        var order = codes.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        var sorted = items.OrderBy(i => order.IndexOf(i.Code) is var ix && ix >= 0 ? ix : order.Count + i.Sort).ToList();
        for (var i = 0; i < sorted.Count; i++) sorted[i].Sort = i;
    }

    static WatchlistDto ToDto(Watchlist w) =>
        new(w.Id, w.Name, w.Sort, w.Items.OrderBy(i => i.Sort).Select(i => new WatchlistItemDto(i.Code, i.Sort)).ToList());

    // ---------- settings ----------

    static void MapSettings(RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (SumireDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
            return rows.ToDictionary(r => r.Key, r => JsonDocument.Parse(r.Value).RootElement.Clone());
        });

        api.MapGet("/settings/{key}", async (SumireDbContext db, string key, CancellationToken ct) =>
            await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct) is { } s
                ? Results.Text(s.Value, "application/json")
                : NotFound($"No setting '{key}'"));

        api.MapPut("/settings/{key}", async (SumireDbContext db, HttpRequest request, string key, CancellationToken ct) =>
        {
            if (!SettingKeyRegex().IsMatch(key)) return Bad("key must be 1-64 lowercase letters, digits, '.', '_' or '-'.");
            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            if (raw.Length > MaxSettingBytes) return Bad("value is too large (64 KB max).");
            string value;
            try { value = JsonDocument.Parse(raw).RootElement.GetRawText(); }
            catch (JsonException) { return Bad("body must be JSON."); }

            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row is null) db.Settings.Add(new Setting { Key = key, Value = value });
            else row.Value = value;
            await db.SaveChangesAsync(ct);
            return Results.Text(value, "application/json");
        });

        api.MapDelete("/settings/{key}", async (SumireDbContext db, string key, CancellationToken ct) =>
            await db.Settings.Where(s => s.Key == key).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : NotFound($"No setting '{key}'"));
    }

    // ---------- alerts ----------

    static void MapAlertRoutes(RouteGroupBuilder api)
    {
        api.MapGet("/alerts", async (SumireDbContext db, CancellationToken ct) =>
            (await db.Alerts.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync(ct)).Select(ToDto));

        api.MapGet("/alerts/recent", (AlertFeed feed) => feed.Recent);

        api.MapPost("/alerts", async (SumireDbContext db, InstrumentCatalog catalog, TimeProvider clock, AlertBody body, CancellationToken ct) =>
        {
            var code = string.IsNullOrWhiteSpace(body.Code) ? null : body.Code.Trim().ToUpperInvariant();
            if (code is not null && catalog.Find(code) is null && !MarketDataService.IsIdxCode(code)) return Bad("Unknown code.");
            var alert = new Alert
            {
                Code = code,
                Type = body.Type?.Trim().ToLowerInvariant() ?? "",
                Params = body.Params?.GetRawText() ?? "{}",
                Active = body.Active ?? true,
                CreatedAt = clock.GetUtcNow(),
            };
            if (AlertRules.Validate(alert) is { } error) return Bad(error);
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/alerts/{alert.Id}", ToDto(alert));
        });

        api.MapPut("/alerts/{id:long}", async (SumireDbContext db, long id, AlertBody body, CancellationToken ct) =>
        {
            var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (alert is null) return NotFound("Alert not found");
            if (ApplyUpdate(alert, body) is { } error) return Bad(error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(alert));
        });

        api.MapDelete("/alerts/{id:long}", async (SumireDbContext db, long id, CancellationToken ct) =>
            await db.Alerts.Where(a => a.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : NotFound("Alert not found"));
    }

    /// <summary>
    /// Applies a PUT body. Switching an alert back on or changing its params re-arms it, so it can fire again today.
    /// Returns an error message (and leaves the alert unchanged) when the result is invalid.
    /// </summary>
    public static string? ApplyUpdate(Alert alert, AlertBody body)
    {
        var updated = new Alert { Code = alert.Code, Type = alert.Type, Params = body.Params?.GetRawText() ?? alert.Params, Active = body.Active ?? alert.Active };
        if (AlertRules.Validate(updated) is { } error) return error;

        var paramsChanged = !AlertRules.SameParams(updated.Params, alert.Params);
        if (updated.Active && (!alert.Active || paramsChanged)) alert.LastTriggeredAt = null;
        alert.Params = updated.Params;
        alert.Active = updated.Active;
        return null;
    }

    static AlertDto ToDto(Alert a) => new(a.Id, a.Code, a.Type, JsonDocument.Parse(a.Params).RootElement.Clone(), a.Active,
        Wib.ToWib(a.CreatedAt), a.LastTriggeredAt is { } t ? Wib.ToWib(t) : null);

    // ---------- helpers ----------

    static IResult Bad(string detail) => Results.Problem(statusCode: 400, title: "Invalid request", detail: detail);
    static IResult NotFound(string title) => Results.Problem(statusCode: 404, title: title);

    /// <summary>These endpoints all need the database; an outage becomes a 503 ProblemDetails.</summary>
    static async ValueTask<object?> DbUnavailableFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!Db.IsConfigured)
            return Results.Problem(statusCode: 503, title: "Database unavailable", detail: Db.NotConfiguredReason);
        try
        {
            return await next(ctx);
        }
        catch (Exception ex) when (!ctx.HttpContext.RequestAborted.IsCancellationRequested
            && (ex is DbException or DbUpdateException or TimeoutException or System.Net.Sockets.SocketException || ex.InnerException is DbException or System.Net.Sockets.SocketException))
        {
            return Results.Problem(statusCode: 503, title: "Database unavailable", detail: Db.ShortError(ex));
        }
    }
}
