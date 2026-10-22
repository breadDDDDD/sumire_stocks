namespace Sumire.Api.Market;

public static class MarketModule
{
    public static IServiceCollection AddMarket(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapMarket(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
