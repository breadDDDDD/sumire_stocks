namespace Sumire.Api.Research.Mcp;

/// <summary>`Sumire mcp`: stdio MCP server. stdout carries the protocol, so log and print to stderr only.</summary>
public static class McpCommand
{
    public static Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        Console.Error.WriteLine("mcp: not implemented yet");
        return Task.FromResult(0);
    }
}
