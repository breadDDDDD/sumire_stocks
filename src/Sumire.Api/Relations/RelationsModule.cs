using Sumire.Api.Market;

namespace Sumire.Api.Relations;

public static class RelationsModule
{
    public static IServiceCollection AddRelations(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<RelationsMapProvider>();
        services.AddSingleton(sp => new RelationsStatsStore(sp.GetRequiredService<ILogger<RelationsStatsStore>>()));
        services.AddScoped<RelationsStatsStep>();
        services.AddScoped<IEodPipelineStep>(sp => sp.GetRequiredService<RelationsStatsStep>());
        services.AddScoped<RelationsService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRelations(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/stocks/{code}/relations", async (RelationsService relations, string code, CancellationToken ct) =>
            await relations.GetAsync(code, ct) is { } r
                ? Results.Ok(r)
                : Results.Problem(statusCode: 404, title: $"Unknown code {code.Trim().ToUpperInvariant()}"));

        api.MapGet("/relations/factors", (RelationsService relations) =>
            relations.Map.FactorNames.Order().ToList());

        api.MapGet("/relations/factors/{factor}", async (RelationsService relations, string factor, CancellationToken ct) =>
            await relations.GetFactorAsync(factor, ct) is { } r
                ? Results.Ok(r)
                : Results.Problem(statusCode: 404, title: $"Unknown factor {factor.Trim().ToUpperInvariant()}"));

        return app;
    }
}
