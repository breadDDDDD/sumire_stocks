using System.Net;
using Sumire.Api.Jobs;
using Sumire.Api.Market;
using Sumire.Api.Market.Rss;

namespace Sumire.Api.Alerts;

/// <summary>Watchlists, settings, alerts (+ AlertJob and the EOD alert step) and RSS headlines.</summary>
public static class AlertsModule
{
    public static IServiceCollection AddAlerts(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient<RssNewsClient>(c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(YahooClient.UserAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml,application/xml,text/xml;q=0.9,*/*;q=0.8");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
            .AddStandardResilienceHandler();

        services.AddSingleton<AlertFeed>();
        services.AddScoped<AlertService>();
        services.AddScoped<IEodPipelineStep, AlertEodStep>();
        services.AddHostedService<AlertJob>();
        return services;
    }

    public static IEndpointRouteBuilder MapAlerts(this IEndpointRouteBuilder app)
    {
        app.MapAlertsEndpoints();
        return app;
    }
}
