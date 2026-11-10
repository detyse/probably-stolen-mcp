# Probably Stolen MCP — Probably Automated

[简体中文](README.zh-CN.md) · [User guide](docs/usage-guide.md) · [Agent integration](#building-an-agent)

Let your AI agent play *Probably Stolen* through MCP. The project provides a game Mod and a local MCP server for reading the current game state, inspecting items and screenshots, and performing available game actions. Your AI client supplies the model and decisions.

**Experimental · version 0.2.7 · bridge protocol 2.** The in-game Mod is named **Probably Automated**. Supported scenarios and real-game evidence are tracked in the [capability record](docs/capabilities.md), [profession tests](docs/profession-tests.md), and [overnight production tests](docs/overnight-tests.md). Coverage varies by game version and scenario.

## Get started

You need Windows x64, an installed Steam copy of *Probably Stolen*, [uv](https://docs.astral.sh/uv/getting-started/installation/) to manage Python 3.11+ and dependencies, and an AI client that can start local **stdio MCP** servers. A client and model that support image tool results can also inspect game screenshots.

Download `probably-stolen-mcp-0.2.7-windows-x64.zip` from [Releases](https://github.com/detyse/probably-stolen-mcp/releases/tag/v0.2.7) and extract it to a permanent folder. This experimental package contains the compiled Mod and Python MCP source with locked dependencies; using it does not require the .NET SDK. The first MCP launch needs internet access to prepare Python and its dependencies. GitHub's separate **Source code** archives require the [build steps](docs/development.md#build-from-source).

### 1. Install the Mod

1. Exit the game. In Steam, use **Browse local files** to open its installation directory.
2. Copy `artifacts/mod/probably-stolen-mcp` into the game's `Mods` directory. The resulting `Mods/probably-stolen-mcp` folder should contain `manifest.xml`, `PSMcp.Mod.dll`, and `PSMcp.Contracts.dll`.
3. Start the game, enable **Probably Automated** in the Mods menu, and restart when prompted.

Keep the extracted release folder or source checkout available for the Python MCP service. uv prepares its Python environment on first launch. For upgrades from PawnPilot, see the [installation guide](docs/usage-guide.md#3-install-the-mod).

### 2. Connect your AI client

In PowerShell at the extracted release root, run `./scripts/Configure-Mcp.ps1` to generate `artifacts/mcp-config.json` using your local paths. Merge its `probably-stolen-mcp` entry into your client's MCP configuration. Run the script again if you move the folder. Source builds also generate this file automatically.

For clients using `mcpServers`, the configuration has this structure. Replace the placeholders with the actual paths from your generated file.

```json
{
  "mcpServers": {
    "probably-stolen-mcp": {
      "command": "<absolute-path-to-uv.exe>",
      "args": [
        "--directory",
        "<absolute-repo-path>",
        "run",
        "--locked",
        "--no-dev",
        "probably-stolen-mcp"
      ]
    }
  }
}
```

If the client has a configuration form, choose **stdio / local command** and enter the same `command` and `args`. Refresh the MCP connection or restart the client. The client starts and manages the MCP process.

The Mod's `http://127.0.0.1:15527` address is an internal bridge. Configure the client with the stdio launch command above. Custom connection-file locations are covered in the [client setup guide](docs/usage-guide.md#4-connect-your-ai-client).

### 3. Try it

Start the game and send this to your agent.

> Use probably-stolen-mcp. Read get_help, then call get_bridge_status and get_game_state. Report whether the bridge is connected, whether a save is loaded, and the current day, cash, and available actions. Only inspect state during this check.

At the main menu, `gameLoaded: false` is expected. Load a test save, then try one action.

> Read the latest state. If open_shop is available, execute it once and read the state again to confirm the shop opened. If the action is unavailable or the result is uncertain, explain why.

Actions affect the current game and may be autosaved. Use a test save for your first session. For longer tasks, see the [business-day example](docs/usage-guide.md#6-complete-a-business-day) and [item and night-outing examples](docs/usage-guide.md#7-showcases-placement-trash-and-night-outings).

## Building an agent

Connect through MCP and use **observe → choose a current action → execute → verify**. Game rules, legal targets, and argument constraints come from the Mod. Your agent handles planning, interpreting evidence, and deciding whether the result meets the task.

### Tools

| Tool | Purpose |
| --- | --- |
| `get_help(topic?)` | Read bundled instructions and topic listings, even when the game is closed |
| `get_bridge_status` | Check the authenticated connection to the Mod |
| `get_game_state` | Read the current day, phase, cash, customer, inventory, interface, and available actions |
| `get_trade_state` | Inspect the current counter, visible quotes, and trade interaction |
| `get_inventory(containerId?, offset?, limit?)` | Read presented containers and opened contents, with pagination |
| `get_available_actions` | Get current actions, parameter constraints, `snapshotId`, limitations, and usage notes |
| `get_ui_state` | Read visible text, controls, disabled reasons, and interface actions |
| `get_entity(entityId)` | Inspect an item, container, or control already exposed by the current session |
| `get_view` | Get a game screenshot and its corresponding observation |
| `get_action_status(requestId)` | Look up a retained command result without executing it again |
| `perform_action(action, expectedSnapshotId, requestId, arguments?)` | Perform one currently permitted native game action |

### Action loop

1. Read the latest state. `get_game_state` nests its observation under `state`; `get_available_actions` returns `snapshotId` and `availableActions` at the top level.
2. Copy an exact `availableActions[].name`. Fill `arguments` using that action's `parameters`, including its required fields, ranges, and candidate references.
3. Call `perform_action` with that observation's `snapshotId` and a new UUID as `requestId`. The following is a template for an action with no arguments.

```json
{
  "action": "<exact availableActions[].name>",
  "expectedSnapshotId": "<snapshotId from the same observation>",
  "requestId": "<new UUID>"
}
```

4. Observe again to verify the effect. `dispatched` means the handler ran; animation, confirmation, or production may still be in progress.
5. On `stale_snapshot`, observe again and make a new decision with a new UUID. After a timeout or disconnect, query `get_action_status` and the current state first. A retry of the original command must preserve its UUID, action, snapshot, and complete arguments.

Result history holds the latest 256 commands during the Mod session. A missing result does not prove an action never happened. After changing saves or restarting the game, obtain fresh observations and references. When no action is available, inspect the waiting state and limitations before proceeding.

Use `get_help(topic="actions")` for calling rules. Other topics include `items`, `appraisal`, `machines`, `trade`, `night`, `menus`, and `capabilities`. Bundled help is currently primarily in Chinese; the [English user guide](docs/usage-guide.md#8-agent-calling-rules) covers operating rules.

### Extending the integration

Use the current action list to drive interactions. For a new game interaction or MCP tool, see the [extension guide](docs/development.md#extend-the-agent-interface), [protocol models](src/PSMcp.Contracts), and [MCP tools](mcp/psmcp). The [implementation notes](docs/mcp-implementation.md) describe the existing adapters and their limits.

## Help and data

If the Mod is missing, the bridge cannot connect, or an action fails, see [troubleshooting](docs/usage-guide.md#9-troubleshooting). Include the game Build, Mod/MCP version, tool name, error code, and reproduction steps when reporting a problem.

The Mod connection stays on the local machine. Your AI client receives requested game state and images and may forward them to its model provider. Keep the control token in `connection.json` private and remove personal paths from shared logs.

Original code and documentation use the [MIT License](LICENSE). Game content retains its owners' rights. This is an independent project. Mod development follows the [official modding guide](https://github.com/dragonpan2/probably-stolen-modding), with architecture inspiration from [STS2MCP](https://github.com/Gennadiyev/STS2MCP).
