using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PSMcp.Contracts;
using PSMcp.Server;

if (args.Contains("--help"))
{
    Console.WriteLine("Probably Stolen MCP server (stdio)\nUsage: PSMcp.Server [--connection <connection.json>]\nEnvironment: PSMCP_CONNECTION_FILE overrides the default Unity ModData path.\nEnable the game Mod first; it creates connection.json on startup.");
    return;
}

string? connectionFile = Environment.GetEnvironmentVariable("PSMCP_CONNECTION_FILE");
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--connection" && i + 1 < args.Length) connectionFile = args[++i];
    else
    {
        Console.Error.WriteLine("Unknown or incomplete argument. Use --help.");
        Environment.ExitCode = 2;
        return;
    }
}
connectionFile ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "AppData", "LocalLow", "Questing Goose Studio", "Probably Stolen", "ModData", Protocol.ModId, "connection.json");

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new BridgeClient(connectionFile));
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<GameTools>();
await builder.Build().RunAsync();
