using Sumire.Api.Analytics.Services;
using Sumire.Api.Jobs;
using Sumire.Api.Market;

namespace Sumire.Api.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalytics(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<AnalyticsStore>();
        services.AddScoped<FundamentalsService>();
        services.AddScoped<IEodPipelineStep, AnalyticsSnapshotStep>();
        services.AddScoped<IEodPipelineStep, SignalScoreStep>();
        services.AddHostedService<AnalyticsWarmup>();
        services.AddHostedService<FundamentalsJob>();
        return services;
    }

    public static IEndpointRouteBuilder MapAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapAnalyticsEndpoints();
        return app;
    }
}

/// <summary>Builds the day's snapshot shortly after startup so the first screen loads instantly.</summary>
public sealed class AnalyticsWarmup(AnalyticsStore store, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), clock, ct);
        try { await store.GetAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { /* logged by the store; requests retry */ }
    }
}
