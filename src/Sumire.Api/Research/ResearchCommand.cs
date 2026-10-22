namespace Sumire.Api.Research;

/// <summary>`Sumire research --once`: processes due research jobs and exits.</summary>
public static class ResearchCommand
{
    public static Task<int> RunOnceAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        Console.WriteLine("research --once: not implemented yet");
        return Task.FromResult(0);
    }
}
