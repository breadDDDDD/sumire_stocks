namespace Sumire.Api.Forecasting;

public static class ForecastingModule
{
    public static IServiceCollection AddForecasting(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapForecasting(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
