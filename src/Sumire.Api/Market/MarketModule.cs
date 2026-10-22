using System.Net;
using Sumire.Api.Hubs;
using Sumire.Api.Jobs;

namespace Sumire.Api.Market;

public static class MarketModule
{
    public static IServiceCollection AddMarket(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<YahooSession>();
        services.AddHttpClient<IMarketDataProvider, YahooClient>(c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(YahooClient.UserAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
                c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = sp.GetRequiredService<YahooSession>().Cookies,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler();

        services.AddSingleton(_ => InstrumentCatalog.FromSeed());
        services.AddSingleton<MarketStatus>();
        services.AddSingleton<QuoteSubscriptions>();
        services.AddScoped<MarketDataService>();

        services.AddSingleton<EodPipelineJob>();
        services.AddHostedService(sp => sp.GetRequiredService<EodPipelineJob>());
        services.AddHostedService<IntradayQuoteJob>();
        return services;
    }

    public static IEndpointRouteBuilder MapMarket(this IEndpointRouteBuilder app)
    {
        app.MapMarketEndpoints();
        app.MapHub<QuotesHub>(QuotesHub.Path);
        return app;
    }
}
