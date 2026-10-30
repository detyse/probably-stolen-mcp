using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PawnPilot.Mcp;

if (args.Contains("--help"))
{
    Console.WriteLine("PawnPilot MCP server (stdio)\nUsage: PawnPilot.Mcp [--connection <connection.json>]\nEnvironment: PAWNPILOT_CONNECTION_FILE overrides the default Unity ModData path.\nEnable the game Mod first; it creates connection.json on startup.");
    return;
}

string? connectionFile = Environment.GetEnvironmentVariable("PAWNPILOT_CONNECTION_FILE");
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
    "AppData", "LocalLow", "Questing Goose Studio", "Probably Stolen", "ModData", "pawnpilot", "connection.json");

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new BridgeClient(connectionFile));
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<GameTools>();
await builder.Build().RunAsync();
