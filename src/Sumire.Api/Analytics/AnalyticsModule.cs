namespace Sumire.Api.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalytics(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapAnalytics(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
