namespace Sumire.Api.Relations;

public static class RelationsModule
{
    public static IServiceCollection AddRelations(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapRelations(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
