using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Sumire.Api.Data;

namespace Sumire.Api.Common;

public static class HealthEndpoints
{
    public static readonly string Version =
        typeof(HealthEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public record HealthResponse(string Status, string Db, string? DbError, string Time, string Version);

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (SumireDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            string? error = null;
            if (!Db.IsConfigured)
            {
                error = Db.NotConfiguredReason;
            }
            else
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await db.Database.OpenConnectionAsync(timeout.Token);
                    await db.Database.CloseConnectionAsync();
                }
                catch (Exception ex)
                {
                    error = Db.ShortError(ex);
                }
            }

            return new HealthResponse(
                Status: error is null ? "ok" : "degraded",
                Db: error is null ? "ok" : "error",
                DbError: error,
                Time: Wib.Iso(clock.GetUtcNow()),
                Version: Version);
        });
        return app;
    }
}
