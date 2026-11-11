# Probably Stolen MCP

[Simplified Chinese](README.zh-CN.md) · [User guide](docs/usage-guide.md) · [Agent integration](#building-an-agent)

Probably Stolen MCP lets you ask your AI assistant what's in your shop, inspect a customer's offer, or open for business in *Probably Stolen*.

## Get started

You'll need the Steam version of *Probably Stolen* on Windows x64 and an AI client that supports local MCP servers.

### 1. Install the Mod

1. [Download the mod ZIP](https://github.com/detyse/probably-stolen-mcp/releases/download/v0.2.7/probably-stolen-mcp-0.2.7-mod.zip) and open it.
2. Close the game. In your Steam library, right-click *Probably Stolen* and choose **Manage → Browse local files**.
3. Copy the **Mods** folder from the ZIP into the game folder. If you're updating, replace the files when Windows asks.
4. Start the game, enable **Probably Stolen MCP** in the **Mods** menu, and accept the network permission so your AI client can connect. Restart the game.

Upgrading from PawnPilot? Follow the [migration steps](docs/usage-guide.md#3-install-the-mod).

### 2. Connect your AI client

1. [Install uv](https://docs.astral.sh/uv/getting-started/installation/). It sets up Python and the required packages the first time your AI client connects.
2. [Download the MCP server source](https://github.com/detyse/probably-stolen-mcp/archive/refs/heads/main.zip) and unzip it somewhere you can keep it. Open PowerShell in the extracted folder that contains `pyproject.toml`.
3. Run the following command to generate the settings for your computer.

   ```powershell
   .\scripts\Configure-Mcp.ps1
   ```

4. Open the generated `artifacts/mcp-config.json`. In your client's MCP settings, add the `probably-stolen-mcp` entry under `mcpServers`. If the client uses a form, choose **stdio** (local command) and copy the `command` and `args` values.
5. Refresh the client's MCP connections or restart the client. Keep the server folder where it is. If you move it, run the script again and update the client settings.

### 3. Try it

Load a save and try this question.

> How much cash do I have, and what's in my shop?

Once your assistant can see the shop, you can ask it to take an action, such as opening for business. The game may save those changes, so use a test save for your first session. See the [business-day walkthrough](docs/usage-guide.md#6-complete-a-business-day) for a longer example.

Version 0.2.7 is experimental. See [tested features](docs/capabilities.md) for gameplay coverage by version.

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
