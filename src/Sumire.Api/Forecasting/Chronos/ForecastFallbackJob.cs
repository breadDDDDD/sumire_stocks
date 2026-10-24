using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Forecasting.Chronos;

/// <summary>
/// Local fallback (FORECAST_LOCAL_FALLBACK=true): at 18:00 WIB on trading days, if the nightly GitHub Action has
/// not written today's Chronos forecasts, runs <c>forecast --once</c> as a child process so the model's memory
/// is released when it exits. Never loads ONNX Runtime itself.
/// </summary>
public sealed class ForecastFallbackJob(
    IServiceScopeFactory scopes,
    MarketCalendar calendar,
    TimeProvider clock,
    ILogger<ForecastFallbackJob> logger) : BackgroundService
{
    public static readonly TimeOnly CheckAt = new(18, 0);
    static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(30);

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("FORECAST_LOCAL_FALLBACK"), "true", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Environment.GetEnvironmentVariable("FORECAST_MODE"), "off", StringComparison.OrdinalIgnoreCase)
        && Db.IsConfigured;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(30), clock, ct);
        while (!ct.IsCancellationRequested)
        {
            var now = calendar.NowWib;
            var today = DateOnly.FromDateTime(now.DateTime);
            if (calendar.IsTradingDay(today) && TimeOnly.FromDateTime(now.DateTime) >= CheckAt)
                await CheckAsync(today, ct);

            var wait = NextCheck(now) - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
        }
    }

    /// <summary>Next 18:00 WIB on a trading day strictly after <paramref name="nowWib"/>.</summary>
    public DateTimeOffset NextCheck(DateTimeOffset nowWib)
    {
        var day = DateOnly.FromDateTime(nowWib.DateTime);
        if (!calendar.IsTradingDay(day) || TimeOnly.FromDateTime(nowWib.DateTime) >= CheckAt) day = calendar.NextTradingDay(day);
        return new DateTimeOffset(day.ToDateTime(CheckAt), TimeSpan.FromHours(7));
    }

    async Task CheckAsync(DateOnly today, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SumireDbContext>();
            if (await db.Forecasts.AnyAsync(f => f.Model == ChronosForecaster.ModelName && f.AsOf == today, ct)) return;

            logger.LogInformation("No {Model} forecasts for {Date}; running the local fallback", ChronosForecaster.ModelName, today);
            var exit = await RunChildAsync(ct);
            logger.Log(exit == 0 ? LogLevel.Information : LogLevel.Warning, "Local forecast run exited with {Code}", exit);
            // Let the signal scores pick up the new model rows.
            if (exit == 0) await scope.ServiceProvider.GetRequiredService<Analytics.Services.AnalyticsStore>().RescoreAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Forecast fallback check failed: {Error}", Db.ShortError(ex));
        }
    }

    async Task<int> RunChildAsync(CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? "dotnet";
        var args = "forecast --once";
        // Running as `dotnet Sumire.dll`: pass the dll to the host.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            args = $"\"{Path.Combine(AppContext.BaseDirectory, "Sumire.dll")}\" {args}";

        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppPaths.ContentRoot,
        })!;
        _ = p.StandardOutput.ReadToEndAsync(ct);
        _ = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ChildTimeout);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            throw;
        }
        return p.ExitCode;
    }
}
