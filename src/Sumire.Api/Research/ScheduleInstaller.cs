namespace Sumire.Api.Research;

/// <summary>`Sumire install-schedule` / `uninstall-schedule`: Windows Task Scheduler entries for `research --once`.</summary>
public static class ScheduleInstaller
{
    public static Task<int> InstallAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        Console.WriteLine("install-schedule: not implemented yet");
        return Task.FromResult(0);
    }

    public static Task<int> UninstallAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        Console.WriteLine("uninstall-schedule: not implemented yet");
        return Task.FromResult(0);
    }
}
