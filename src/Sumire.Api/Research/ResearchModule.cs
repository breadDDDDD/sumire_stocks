namespace Sumire.Api.Research;

public static class ResearchModule
{
    public static IServiceCollection AddResearch(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapResearch(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
