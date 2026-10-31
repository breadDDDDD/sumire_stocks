using Sumire.Api.Data;
using Sumire.Api.Market;

namespace Sumire.Api.Common;

public static class CoreServices
{
    /// <summary>Services every mode (web and CLI verbs) needs: clock, cache, DB, IDX rules and calendar.</summary>
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSumireDb();
        services.AddScoped<StorageGuard>();
        services.AddScoped<IEodPipelineStep, StorageGuardStep>();
        services.AddSingleton(sp => IdxRules.Load(clock: sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => MarketCalendar.Load(sp.GetRequiredService<IdxRules>(), clock: sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
