# Probably Stolen MCP — Probably Automated

[简体中文](README.md) · [English user guide](docs/usage-guide.md) · [中文使用指南](docs/usage-guide.zh-CN.md)

An independent C# Mod and MCP server that lets an AI client observe and play *Probably Stolen* through native game actions. Bring your own MCP client and model.

**Status: experimental, version 0.2.7 / bridge protocol 2.** Suitable for developers and players willing to build from source. This is not a claim of complete gameplay or game-version coverage.

## Start here

Follow the [user guide](docs/usage-guide.md) for installation, client configuration, a first action, a full business-day task, inventory placement, night outings, and troubleshooting.

Requirements: Windows x64, an installed Steam copy of the game, the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), PowerShell, and an AI client that can start local stdio MCP servers.

From the repository root:

~~~powershell
.\scripts\Build.ps1 -GamePath 'D:\Apps\Steam\steamapps\common\Probably Stolen'
~~~

Adjust the game path. The command builds, tests, and produces:

- `artifacts/mod/probably-stolen-mcp/`: copy its three files into the game's `Mods/probably-stolen-mcp/`, enable **Probably Automated**, and restart the game.
- `artifacts/mcp/`: keep the full directory outside the game Mods folder.
- `artifacts/mcp-config.json`: merge its server entry into your AI client's configuration.

The displayed Mod name is **Probably Automated**. Assembly names remain `PSMcp.*`; the Mod ID and MCP configuration key remain `probably-stolen-mcp`; environment variables use `PSMCP_*`.

## How it works

~~~text
AI client + model
    -> stdio MCP: PSMcp.Server (.NET 10)
    -> authenticated loopback HTTP: 127.0.0.1:15527
    -> PSMcp.Mod (.NET Standard 2.1)
    -> native game operations on Unity's main thread
~~~

The HTTP port is an internal bridge, not a remote MCP endpoint. The AI client starts the stdio server.

There are 11 tools: `get_help`, `get_bridge_status`, `get_game_state`, `get_trade_state`, `get_inventory`, `get_available_actions`, `get_ui_state`, `get_entity`, `get_view`, `get_action_status`, and `perform_action`.

Use **observe → choose a current action → execute → verify**. The server returns permitted actions and argument constraints. The client need not implement separate click rules for each page. Unsupported custom controls may still require adaptation. Built-in help works without a running game and is currently primarily in Chinese; the English guide covers operating rules.

## Capabilities and test records

See the [capability record](docs/capabilities.md), [profession test report](docs/profession-tests.md), [overnight production report](docs/overnight-tests.md), and [publication assessment](docs/public-readiness.md) for supported scenarios, evidence, and limitations. The [interaction catalog](docs/game-interactions.md) is a design reference.

## Data and game effects

Actions affect your current run and may be autosaved. Use a test save for your first session.

The bridge listens only on loopback and authenticates each request. Keep `connection.json` private; the Mod rotates its token on restart. The project implements no separate telemetry uploader. Your AI client receives requested game state and images; a cloud-model client may send these results to its provider under that client's settings.

## Development

Run protocol, HTTP, stdio, and bundled-help checks without any game files:

~~~powershell
.\scripts\Test.ps1
~~~

The [CI workflow](.github/workflows/ci.yml) runs this suite on Windows with .NET 10. It does not compile the game-dependent Mod or play the game. Full Mod builds use local game assemblies as references and do not package them.

To report a bug, include game Build, Mod/MCP version, tool name, error code, and reproduction steps. Remove tokens and personal paths from logs. Implementation details and the evidence ledger are currently in Chinese.

## License and attribution

Original project code and documentation are under the [MIT License](LICENSE). Game names, in-game text, and gameplay screenshots belong to their respective rights holders and are not additionally licensed by this repository's MIT license. Game binaries, decompiled source, and extracted assets are not distributed here. Dependencies retain their own licenses.

This is an independent project, not an official game release. The [official modding code of conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md) encourages open-source mods and defines distribution rules.
