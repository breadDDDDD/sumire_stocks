using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Sumire.Api.Alerts;
using Sumire.Api.Analytics;
using Sumire.Api.Common;
using Sumire.Api.Data;
using Sumire.Api.Forecasting;
using Sumire.Api.Market;
using Sumire.Api.Relations;
using Sumire.Api.Research;
using Sumire.Api.Research.Mcp;

if (AppPaths.FindEnvFile() is { } envFile)
    DotNetEnv.Env.Load(envFile, new DotNetEnv.LoadOptions(clobberExistingVars: false));

var verb = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : "web";
var rest = verb == "web" && args.Length > 0 && args[0].StartsWith('-') ? args : args.Skip(1).ToArray();
if (args is ["-h" or "--help", ..]) verb = "help";

return verb switch
{
    "web" => await RunWebAsync(rest),
    "forecast" => await RunCliAsync(rest, ForecastCommand.RunOnceAsync),
    "research" => await RunCliAsync(rest, ResearchCommand.RunOnceAsync),
    "install-schedule" => await RunCliAsync(rest, ScheduleInstaller.InstallAsync),
    "uninstall-schedule" => await RunCliAsync(rest, ScheduleInstaller.UninstallAsync),
    "mcp" => await RunCliAsync(rest, McpCommand.RunAsync, logToStderr: true),
    "help" => Usage(0),
    _ => Usage(1),
};

static int Usage(int code)
{
    Console.Error.WriteLine("""
        Usage: Sumire [verb]
          (none)               web server + UI on http://localhost:5080
          forecast --once      run foundation-model forecasts and exit
          research --once      process due research jobs and exit
          install-schedule     register research --once in Windows Task Scheduler
          uninstall-schedule   remove the Task Scheduler entries
          mcp                  stdio MCP server
        """);
    return code;
}

static void AddAreas(IServiceCollection services, IConfiguration config)
{
    services.AddCore();
    services.AddMarket(config);
    services.AddAnalytics(config);
    services.AddForecasting(config);
    services.AddResearch(config);
    services.AddRelations(config);
    services.AddAlerts(config);
}

// CLI verbs: a host without Kestrel. It is never started, so BackgroundServices registered by areas do not run.
static async Task<int> RunCliAsync(string[] args, Func<IServiceProvider, string[], CancellationToken, Task<int>> command, bool logToStderr = false)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppPaths.ContentRoot });
    if (logToStderr)
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    AddAreas(builder.Services, builder.Configuration);

    using var host = builder.Build();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    using var scope = host.Services.CreateScope();
    return await command(scope.ServiceProvider, args, cts.Token);
}

static async Task<int> RunWebAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppPaths.ContentRoot });

    builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(5080));
    builder.Services.Configure<JsonOptions>(o =>
    {
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
    builder.Services.AddProblemDetails();
    builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
    builder.Services.AddSignalR().AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
    AddAreas(builder.Services, builder.Configuration);

    var app = builder.Build();

    await Db.TryMigrateAsync(app.Services, app.Logger);

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseResponseCompression();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapHealth();
    app.MapMarket();
    app.MapAnalytics();
    app.MapForecasting();
    app.MapResearch();
    app.MapRelations();
    app.MapAlerts();

    // Unknown API routes stay 404 instead of falling through to the SPA.
    app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Not found"));
    if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
        app.MapFallbackToFile("index.html");
    else
        app.MapFallback(() => Results.Text("Sumire API is running. The UI is not built yet: run `npm run build` in web/ (or `npm run dev` and open http://localhost:5173).", "text/plain"));

    app.Logger.LogInformation("Sumire {Version} listening on http://localhost:5080", HealthEndpoints.Version);
    await app.RunAsync();
    return 0;
}
