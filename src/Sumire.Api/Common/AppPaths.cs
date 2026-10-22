namespace Sumire.Api.Common;

/// <summary>Locates the project folder in development and falls back to the exe folder when published.</summary>
public static class AppPaths
{
    /// <summary>src/Sumire.Api when running from a source checkout, otherwise null.</summary>
    public static readonly string? ProjectDir = FindUpward(AppContext.BaseDirectory, d => File.Exists(Path.Combine(d, "Sumire.Api.csproj")));

    /// <summary>Content root: the project folder in development (so wwwroot is found), the exe folder when published.</summary>
    public static string ContentRoot => ProjectDir ?? AppContext.BaseDirectory;

    public static string? FindUpward(string start, Func<string, bool> match)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (match(dir.FullName)) return dir.FullName;
        return null;
    }

    /// <summary>First .env found walking up from the working directory, then from the exe folder.</summary>
    public static string? FindEnvFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = FindUpward(start, d => File.Exists(Path.Combine(d, ".env")));
            if (dir is not null) return Path.Combine(dir, ".env");
        }
        return null;
    }
}

/// <summary>Seed data folder: src/Sumire.Api/Seed in development, &lt;exe&gt;/Seed when published.</summary>
public static class SeedPaths
{
    public static readonly string Root = ResolveRoot();

    public static string File(string name) => Path.Combine(Root, name);

    static string ResolveRoot()
    {
        if (AppPaths.ProjectDir is { } project && Directory.Exists(Path.Combine(project, "Seed")))
            return Path.Combine(project, "Seed");
        return Path.Combine(AppContext.BaseDirectory, "Seed");
    }
}
