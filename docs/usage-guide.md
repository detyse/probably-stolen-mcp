# Probably Stolen MCP user guide

[简体中文](usage-guide.zh-CN.md) · [Project home](../README.md) · [Capabilities and evidence (Chinese)](capabilities.md)

For **0.2.7 development builds / bridge protocol 2**, on Windows x64. This project lets your own AI client operate *Probably Stolen*. Your client supplies the model, account, and inference.

This is an experimental mod. Recorded gameplay covers a business day, showcases, phone calls and upgrades, reporting, scavenging, sorting, and trash containers. That evidence comes from builds before the project rename. The renamed 0.2.6 package additionally tested all 9 startable professions and representative item operations; see the [profession report](profession-tests.md). Version 0.2.7 adds [overnight production verification](overnight-tests.md). A complete business day, full appraisal, and all night destinations have not been comprehensively repeated with the renamed package. See the [capability record](capabilities.md) for the precise scope.

## 1. Prerequisites

You need:

- An installed Steam copy of *Probably Stolen* that starts normally.
- Windows x64 and PowerShell.
- The [uv](https://docs.astral.sh/uv/getting-started/installation/), which manages Python 3.11+ and the MCP dependencies.
- An AI client configured with a model and able to start a **local stdio MCP server**. To inspect game images, both the client and model must support image tool results.

The client needs to start local processes and access the game's connection file.

The project uses the game's built-in Mod loader. The recorded gameplay baseline is Steam Build **26496118**, displaying **VERSION 100-Rev12**. The 0.2.7 release was compiled against Build **26525552**; compilation does not establish real-game compatibility with that newer build.

## 2. Get installation files

Download `probably-stolen-mcp-0.2.7-mod.zip` from [Releases](https://github.com/detyse/probably-stolen-mcp/releases/tag/v0.2.7). This ZIP contains only `Mods/probably-stolen-mcp/` and its three compiled Mod files. Extract it into the game installation directory; no .NET SDK is needed.

For the Python MCP service, clone or download the repository into a separate permanent folder. With uv on PATH, run `./scripts/Configure-Mcp.ps1` from that repository root to generate `artifacts/mcp-config.json`. The first MCP launch downloads Python and dependencies as needed. You can skip the .NET build steps when using the prebuilt Mod. If building the Mod yourself, see the [development guide](development.md#build-from-source); source builds generate these files:

| Location | Purpose |
| --- | --- |
| `artifacts/mod/probably-stolen-mcp/` | Three Mod files to install in the game |
| `artifacts/python/*.whl` | Standalone Python package with bundled help; optional installation route |
| `artifacts/mcp-config.json` | Client configuration generated for your local paths |

Run `./scripts/Configure-Mcp.ps1` again if you move the files to another location or machine.

## 3. Install the Mod

Exit the game first. If upgrading from PawnPilot, disable the old Mod in the game, exit, and move its `Mods/PawnPilot` folder outside `Mods` so two bridges do not compete for the same port.

Use Steam's **Browse local files** option to find the directory containing `Probably Stolen.exe`, then extract the Release ZIP there. If you built the Mod from source instead, replace `<game-install-dir>` below with that path and run from the repository root:

~~~powershell
$gamePath = '<game-install-dir>'
if (!(Test-Path -LiteralPath (Join-Path $gamePath 'Probably Stolen.exe'))) {
    throw 'Set $gamePath to the correct game installation directory first.'
}
$modPath = Join-Path $gamePath 'Mods\probably-stolen-mcp'
New-Item -ItemType Directory -Path $modPath -Force | Out-Null
foreach ($file in @('manifest.xml', 'PSMcp.Mod.dll', 'PSMcp.Contracts.dll')) {
    Copy-Item -LiteralPath (Join-Path '.\artifacts\mod\probably-stolen-mcp' $file) -Destination $modPath -Force
}
~~~

You can also copy the folder manually. The final layout must be:

~~~text
Probably Stolen/
  Probably Stolen.exe
  Mods/
    probably-stolen-mcp/
      manifest.xml
      PSMcp.Mod.dll
      PSMcp.Contracts.dll
~~~

Start the game, enable **Probably Stolen MCP** in the Mods menu, follow the game's prompts for its declared `network` capability, and restart. For earlier downloads, the installed manifest’s `<Name>` gives the menu label. Keep the separate source checkout available for the Python MCP service.

When the Mod starts successfully, it normally creates:

~~~text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json
~~~

Use the actual path reported in the Mod log. This file contains the current control token; the MCP server reads it automatically. Do not paste it into chats, issues, or the repository. The token changes each time the Mod restarts.

## 4. Connect your AI client

Open the generated `artifacts/mcp-config.json` and merge its `probably-stolen-mcp` entry into the client's MCP configuration, keeping your other servers.

For clients using the `mcpServers` JSON format, the structure is shown below. Each `<…>` placeholder represents an absolute path. Use the actual paths from your generated file:

~~~json
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
~~~

If the client has a configuration form, choose **stdio / local command** and enter the same `command` and `args`. Refresh the MCP connection or restart the client.

**Do not enter `http://127.0.0.1:15527` as a remote MCP URL.** It is the Mod's internal HTTP bridge; the MCP server uses stdio. The client normally starts the server for you.

For a different connection-file location, append `"--connection", "absolute path to connection.json"` to `args`, or set the server process environment variable `PSMCP_CONNECTION_FILE`. The command-line option takes precedence. Change the bridge port in the game's Mod settings and restart to apply it.

## 5. Verify the connection and perform one action

Send this to your AI client:

> Use probably-stolen-mcp. Read get_help first, then call get_bridge_status and get_game_state. Report whether the bridge is connected, whether a save is loaded, the day, cash, and available actions. Only inspect state during this check.

The current build provides 11 tools. `get_help` should work even when the game is closed. Listing tools alone does not prove the game is connected; `get_bridge_status` must return `success: true`. At the main menu, `state.gameLoaded: false` is normal.

Load a save you are comfortable using for experiments and keep the game running. Actions normally affect items, money, time, and autosaves. Then try:

> Read the latest state. If open_shop is in availableActions, execute it once and read the state again to confirm the shop opened. If it is already open, report the current phase without serving another customer.

You do not need to write JSON for ordinary use. Your AI agent selects actions and arguments from the tool results.

## 6. Complete a business day

On a test save, send:

> Use probably-stolen-mcp to open the shop, serve one customer and complete one trade, dismiss the customer, close the shop, and sleep until the next day. Normal game actions, costs, and autosaving within this workflow are authorized. Read the actions, trade, and night help topics first. At each step use the latest availableActions, handle the game's dialogue and confirmations, and check the actual result. Finish by reporting the trade, cash change, and new day. If a required action is missing or the outcome is uncertain, explain where the workflow stopped.

Check the following outcomes:

| Stage | Evidence of completion |
| --- | --- |
| Open the shop | The phase and shutter state change; `dispatched` alone is insufficient |
| Serve and trade | Customer state, quotes, cash, and item ownership change as expected |
| Dismiss and close | The customer leaves; the native confirmation is handled and the night phase begins |
| Sleep | `sleep` opens settlement; after selecting services and confirming, a new observation shows the day increased |

A quoted price is a visible game price, not an item's hidden true value. This is an example task; different customers, events, or game versions may require further adaptation.

## 7. Showcases, placement, trash, and night outings

Give the agent a concrete objective:

| Goal | Example instruction |
| --- | --- |
| Showcase and placement | “Move this sign into the showcase, rotating it if needed, then verify its position and ownership.” |
| Sort and undo | “Sort the current inventory and check the item count, then undo the sort and compare the layout.” |
| Backpack and trash | “Open the backpack, move the specified junk into the trash container, and verify both item counts.” |
| Phone and upgrades | “Call to inspect available upgrades and report costs and prerequisites.” Specify the upgrade when requesting a purchase |
| Reporting | “Use the game's available reporting action for the current customer, then check the police response and receipt.” |
| Night scavenging | “Prepare a backpack, visit the dump, scavenge once, pack the found items, return, and report the result.” |

The game applies its normal costs, capacity, ownership, injuries, false-report penalties, and consequences for abandoned items. Moving something into a trash container differs from clearing it; sleep services may clear trash. Placement uses the current `item_move` action, with legal destination containers, grid positions, rotation, and flipping as allowed by its returned parameters.

Use the current `sleep` action for the bed. Open a backpack before querying its contents. The client does not need separate click rules for every page: the Mod combines game rules and interface state into the current action list. Unsupported custom controls can still lack an action.

### New games and profession items

Have the agent read the menu and follow New game → Profession → Perks → Difficulty/start options → Start. Card isOn reflects selection; disabled cards and coming-soon professions do not authorize continuing. In the store, use navigate_back when listed, observe whether it closed a window or opened pause, then choose Main menu.

Open a profession machine and use containers[].label, windowTitle, and parentItemId to distinguish input/output, seed, water, and animal areas before choosing a legal item_move destination. Icon-only slots can have an empty label. Legal tool targets change with game conditions: a full bottle becomes a water-pack target only after draining. Verify the resulting items; animal processing can take two steps, and overnight production requires normal settlement. See the [profession test results](profession-tests.md).

### Observing overnight production

Rendered player-owned items expose `statusText` from normal native hover sections: production progress, animal/fish condition, water, power, and remaining tool uses. It follows the game language and may contain game rich-text markup. An empty string means these supported sections contain no text; `null` means unavailable or inapplicable, including developer mode. It is limited to 8192 characters and is not a universal recipe API.

Night service controls expose `kind=checkbox`, `isOn`, and the service label/cost. Use the current native `ui_click` action and observe again before confirming sleep. Check the day transition, input consumption, and new output items to establish completion; dispatch or increasing progress alone does not prove production. See the [separate overnight test report](overnight-tests.md).

## 8. Agent calling rules

Use the loop **observe → select an available action → execute → verify**:

1. `get_game_state` nests its data under `state`; `get_available_actions` returns `snapshotId` and `availableActions` at the top level.
2. Copy `availableActions[].name` exactly. Do not substitute `kind`, a description, item name, or numeric ID.
3. Call `perform_action` with that observation's `snapshotId` and a new UUID. Include only arguments and candidate references permitted by the selected action's `parameters`.
4. Observe again after dispatch. `dispatched` means the handler ran; animation, confirmation, or the trade may still be in progress.
5. For `stale_snapshot`, observe again and make a new decision with a new UUID. For a timeout or disconnect, first inspect `get_action_status` and current state. Retrying the original command requires the same UUID, action, snapshot, and complete arguments.

This is a **parameter template**, not an executable request. Replace every placeholder with current values. Omit `arguments` for actions without parameters:

~~~json
{
  "action": "<exact action name>",
  "expectedSnapshotId": "<snapshotId from the same observation>",
  "requestId": "<new UUID>"
}
~~~

Call `get_help(topic="actions")` for details. Other topics include `items`, `appraisal`, `machines`, `trade`, `night`, and `menus`. Bundled help is currently primarily in Chinese; this guide provides English operating instructions. Static documentation never replaces the live action list.

## 9. Troubleshooting

| Symptom | What to check |
| --- | --- |
| `uv` or Python unavailable | Install uv, restart the client, and check its configured command path. The first launch needs access to download Python/dependencies |
| Missing `Assembly-CSharp.dll` | `-GamePath` must point to the game exe's directory, not the repository or Managed subdirectory |
| Mod missing from the menu | Check for an extra nested folder, all three files, and errors in the Mod log |
| `connection_file_unavailable` | Enable the Mod, restart the game, and verify the connection-file path; set `--connection` if needed |
| `bridge_unavailable` | Check that the game and Mod are running and an old Mod is not occupying the port |
| `unauthorized` / 401 | Use the current connection file; do not retain an old token manually |
| `protocol_mismatch` | Update Mod and MCP together from the same build, then restart both |
| Visible text or an image but no action | Inspect `enabled`, `disabledReason`, and `interaction`; the control may be disabled, occluded, or unsupported |
| `stale_snapshot` / `action_unavailable` | Observe again and make a new decision; discard old references |
| `action_outcome_unknown` | Inspect the original UUID and current game state; do not blindly repeat a trade with a new UUID |
| Game query or screenshot times out | Restore a responsive, rendering game window, allow transitions to finish, and observe again |
| Manually launched MCP appears idle | A stdio server waits for client requests; use `--help` to check the launch command |

The [development guide](development.md#debug-the-connection) documents advanced HTTP debugging. When reporting a bug, include the game Build, Mod/MCP versions, reproduction steps, tool name, and error code. Remove tokens, personal paths, and unrelated save information.

## 10. Updating, removal, and data handling

Exit the game and stop the client’s MCP service and any local integration driver before rebuilding, so published DLLs are not locked. Pull the source, rebuild, replace the three Mod files, and start the game and MCP again. If you move the repository, regenerate configuration and update the client's absolute paths. Custom ports and connection-file settings need migration when upgrading from PawnPilot.

To disable control, turn off Probably Stolen MCP in the Mods menu and restart; remove the client's MCP entry if desired. Disabling does not undo actions already taken. This version adds no custom items or save format.

The Mod-to-MCP connection is authenticated and local. Your AI client receives requested game state and game screenshots. If it uses a cloud model, it may send those tool results to that provider, depending on client settings. This project includes no model account and implements no separate telemetry uploader. The connection token is used only for the local bridge.

Original project code and documentation use the [MIT License](../LICENSE). Game names, in-game text, and gameplay screenshots belong to their respective rights holders; this repository's MIT license grants no additional rights to that content. Game binaries, decompiled source, and extracted assets are not provided. See the [official modding code of conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md) for mod development and distribution requirements.
