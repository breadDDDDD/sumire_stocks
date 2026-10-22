using Sumire.Api.Analytics;
using Sumire.Api.Analytics.Services;
using Sumire.Api.Market;

namespace Sumire.Api.Forecasting;

public static class ForecastingModule
{
    public static IServiceCollection AddForecasting(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ForecastService>();
        services.AddScoped<IEodPipelineStep, BaselineForecastStep>();
        services.AddScoped<IEodPipelineStep, ForecastEvaluationStep>();
        return services;
    }

    public static IEndpointRouteBuilder MapForecasting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(AnalyticsEndpoints.UnavailableFilter);

        api.MapGet("/stocks/{code}/forecast", async (AnalyticsStore store, ForecastService forecasts, string code, CancellationToken ct) =>
        {
            var u = await store.GetAsync(ct);
            return await store.GetStockAsync(code, ct) is { } s
                ? Results.Ok(await forecasts.GetAsync(s, u, ct))
                : AnalyticsEndpoints.UnknownCode(code);
        });
        return app;
    }
}
