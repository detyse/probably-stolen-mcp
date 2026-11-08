# Development guide

[简体中文](development.zh-CN.md) · [Project home](../README.md) · [User guide](usage-guide.md)

This guide covers source builds, debugging, and changes to the game bridge or MCP server. Agent usage and calling rules are in the [README](../README.md#building-an-agent).

## Build from source

Use Windows x64, PowerShell, the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), and a local Steam installation of *Probably Stolen*. The SDK includes the runtime. The initial dependency restore needs NuGet access. The Mod targets .NET Standard 2.1 and uses the game's Unity/Mono runtime; the MCP server targets .NET 10.

Clone the repository into any directory, or download and extract the source. Open PowerShell at the repository root, which contains `scripts`.

```powershell
git clone https://github.com/detyse/probably-stolen-mcp.git
Set-Location '.\probably-stolen-mcp'
```

Use Steam's **Browse local files** option to find the directory containing `Probably Stolen.exe`. Replace `<game-install-dir>` below with that path.

```powershell
$gamePath = '<game-install-dir>'
.\scripts\Build.ps1 -GamePath $gamePath
```

The script restores locked NuGet dependencies, compiles the solution, runs protocol tests, and packages the output.

```text
artifacts/
  mod/probably-stolen-mcp/
    manifest.xml
    PSMcp.Mod.dll
    PSMcp.Contracts.dll
  mcp/
    PSMcp.Server.dll
    ... runtime dependencies
  mcp-config.json
```

Use the [installation steps](../README.md#1-install-the-mod) next. Keep all files in `artifacts/mcp` together. The generated MCP configuration contains paths for the build machine; update them when moving the output.

Game assemblies are local compilation references. The Mod package contains only its manifest and the two project DLLs. Unity, Harmony, and Newtonsoft.Json are supplied by the game. Do not redistribute game libraries or extracted assets.

| Option / environment variable | Purpose |
| --- | --- |
| `-GamePath <directory>` | Set the game installation directory |
| `PSMCP_GAME_PATH` | Supply the default when `-GamePath` is omitted |
| `-NoRestore` | Use dependencies already restored for this checkout |
| `-SkipTests` | Package without running tests; this does not establish runtime validation |

Build caches live under `.cache/`. Caches, `bin`, `obj`, and `artifacts` are ignored by Git. Stop the game and client-managed MCP process before rebuilding or replacing DLLs.

## Run checks

Run the protocol, HTTP, stdio, and bundled-help checks without game files.

```powershell
.\scripts\Test.ps1
```

To check only the published help behavior, run the following. `-HelpOnly` and `-Offline` are mutually exclusive.

```powershell
.\scripts\Test.ps1 -HelpOnly
```

The [CI workflow](../.github/workflows/ci.yml) runs the game-independent suite on Windows with .NET 10. Tests use simulated game state, production HTTP bridge code, a real MCP server process, and the official MCP client. They cover scheduling, stale observations, duplicate commands, failures, authentication, timeouts, and tool calls. Game-dependent Mod compilation and real gameplay require separate verification.

Record gameplay evidence in the [capability record](capabilities.md), with the game and Mod versions, scenario, calls, outcomes, and evidence location. Existing results are in the [profession report](profession-tests.md) and [overnight report](overnight-tests.md).

## Architecture

```text
Agent / AI client
    -> MCP over stdio: PSMcp.Server (.NET 10)
    -> authenticated HTTP on 127.0.0.1:15527
    -> PSMcp.Mod (.NET Standard 2.1)
    -> main-thread queue and native game operations
```

Queries use request/response communication. The agent observes again after each action. `get_help` reads bundled documentation independently of the game connection. Screenshots are captured on request.

Game-object access is queued onto Unity's main thread. The normal request queue holds up to 64 entries and handles up to 8 per frame, with a 5-second wait limit. A queued action that times out before execution is cancelled. If execution has already started, an uncertain result must be checked before any retry.

| Location | Responsibility |
| --- | --- |
| [PSMcp.Contracts](../src/PSMcp.Contracts) | Protocol models, snapshots, request deduplication, and queue |
| [PSMcp.Mod](../src/PSMcp.Mod) | Native Mod entry point, game adapters, and local HTTP bridge |
| [PSMcp.Server](../src/PSMcp.Server) | MCP tools, bundled help, and bridge client |
| [PSMcp.SmokeTests](../tests/PSMcp.SmokeTests) | Behavior and protocol integration checks |
| [Build.ps1](../scripts/Build.ps1) | Build, tests, packaging, and client configuration |
| [Inspect-GameApi.ps1](../scripts/Inspect-GameApi.ps1) | Read local game assembly metadata without executing game code |

## Extend the Agent interface

For an Agent using the current interface, start with the [action loop](../README.md#action-loop) and [user guide](usage-guide.md#8-agent-calling-rules). For interface changes, use these entry points.

- Add or adapt native game interactions in [PSMcp.Mod](../src/PSMcp.Mod), keeping discovery and execution consistent with the current game rules and parameter constraints.
- Define shared data in [PSMcp.Contracts](../src/PSMcp.Contracts) and expose MCP tools through [PSMcp.Server](../src/PSMcp.Server). Read the [implementation notes](mcp-implementation.md) before expanding coverage.
- Add help topics as lowercase Markdown filenames in [docs/help](help), starting with a `# Title`. Rebuild and restart the server to embed them. The filename becomes the topic and is listed automatically.
- Update [capabilities](capabilities.md) when changing tools, controls, or limits. Distinguish an implemented entry point from a scenario verified in the real game.

Project names remain `PSMcp.*`, the Mod ID and MCP configuration key are `probably-stolen-mcp`, and environment variables use `PSMCP_*`. The in-game display name is **Probably Automated**.

## Debug the connection

Check the server entry point from the repository root.

```powershell
dotnet '.\artifacts\mcp\PSMcp.Server.dll' --help
```

Without `--help`, the server waits for stdio MCP requests. Logs go to stderr; stdout carries protocol messages.

The Mod normally writes its current bridge address and token here.

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json
```

Use the path reported by the Mod log. For a custom location, append `--connection` and the absolute path to the client's server arguments, or set `PSMCP_CONNECTION_FILE` for that process. The command-line option takes precedence. The token rotates on Mod restart and is reread for each MCP call.

For a read-only HTTP check with the game and Mod running, use the following. This reads the token from disk without printing it.

```powershell
$connectionPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json'
$connection = Get-Content -LiteralPath $connectionPath -Raw | ConvertFrom-Json
$requestArgs = @{
    Uri = $connection.baseUrl + 'health'
    Headers = @{ Authorization = "Bearer $($connection.token)" }
    TimeoutSec = 10
}
Invoke-RestMethod @requestArgs
```

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Check the bridge without accessing game objects |
| GET | `/state` | Read current game state on the main thread |
| GET | `/view` | Request a game screenshot |
| GET | `/actions/<requestId>` | Look up a retained result |
| POST | `/actions` | Submit an action with its snapshot and UUID |

All routes require the current Bearer token. Check JSON `success` even for HTTP 200. POST bodies must have Content-Length and stay within 16 KiB. The bridge uses `TcpListener`, validates the `127.0.0.1` Host, and rejects Origin headers. A complete manual action example is retained in the [Chinese HTTP guide](development.zh-CN.md#手动-http-调用).

Game logs normally live at `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`. Change the Mod's `port` setting and restart if the default port is occupied.

If Windows blocks a DLL with `0x800711C7`, inspect `Microsoft-Windows-CodeIntegrity/Operational` in Event Viewer and the applicable device policy. `-SkipTests` only skips the tests during packaging; runtime loading still needs to succeed. General connection errors are covered in [troubleshooting](usage-guide.md#9-troubleshooting).

## Verify in the game

1. Follow the [official Mod guide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md) to create `dev.txt` beside the executable. Open the F8 developer console and run `mod-scan probably-stolen-mcp`. Check the capability declaration and any rejected operations; the manifest currently declares `network`.
2. Enable the Mod and restart. Check `get_bridge_status` and the unloaded state at the main menu.
3. Load a test save and compare the returned cash, customer, dialogue, controls, and inventory with the game.
4. Perform a currently allowed action and check its actual effect. Retry the identical request to check duplicate protection.
5. Change the game state manually and confirm stale requests are rejected. Repeat relevant checks after changing saves or returning to the main menu.

Use the [official API changes](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.md) and [modding code of conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md) when extending or distributing the Mod. MCP development uses the [official C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
