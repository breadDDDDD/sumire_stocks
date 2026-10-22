namespace Sumire.Api.Forecasting;

/// <summary>`Sumire forecast --once`: runs the foundation-model forecasts and exits.</summary>
public static class ForecastCommand
{
    public static Task<int> RunOnceAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        Console.WriteLine("forecast --once: not implemented yet");
        return Task.FromResult(0);
    }
}
