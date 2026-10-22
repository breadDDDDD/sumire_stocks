namespace Sumire.Api.Alerts;

public static class AlertsModule
{
    public static IServiceCollection AddAlerts(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapAlerts(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
