# Linux and Steam Proton

[简体中文](linux.zh-CN.md) · [README](../README.md) · [Validation status](capabilities.md)

The MCP server runs natively on Linux with Python 3.11+. The Windows Steam game
and its C# Mod run together under Proton. This adds Linux host support; it does
not provide a native Linux game executable. The game uses Steam App **4348910**.
Linux gameplay is experimental; build and protocol tests do not establish
real-game compatibility. On 2026-11-22, live tests verified Mod startup, main-menu reads and screenshots, opening/scrolling/closing options, action-status queries, and a fresh connection after restarting the game. No save or shop gameplay was tested. See the [Linux live validation record](validation/2026-11-22-linux.json).

## Install and connect

1. Install the game through Steam and run it once with Proton. Close it before
   installing the Mod. Steam's **Manage → Browse local files** opens the game directory.
2. Copy the release ZIP's `Mods` directory into the game directory, or build the
   Mod below and copy `artifacts/mod/probably-stolen-mcp` into the game's `Mods`.
   The same managed Mod files are used on Windows and Proton.
3. Start the game, enable **Probably Stolen MCP** in **Mods**, approve its network
   permission, and restart the game. The game's permission prompt must be accepted
   in the game itself.
4. Install [uv](https://docs.astral.sh/uv/getting-started/installation/) on the Linux
   host and keep this repository in a permanent location. From its root, run:

   ```bash
   python3 scripts/Configure-Mcp.py
   ```

5. Add the generated `artifacts/mcp-config.json` entry to the AI client's local
   stdio MCP settings. The client runs Linux `uv`, not a Windows Python under Wine.
   If the repository moves, regenerate the configuration.
6. Refresh the MCP connection and call `get_bridge_status`, then `get_game_state`.
   A missing connection file usually means the Mod has not been enabled and restarted.

No .NET SDK is needed to use a prebuilt Mod. It is required only for source builds
and the C# test suite. `get_help` works without a running game.

For local testing, request a 1280×720 window by default (the game's saved resolution may override the size):

```bash
bash scripts/Run-Game.sh
```

This sets per-launch arguments without changing global Steam settings. Append
`-screen-width 1600 -screen-height 900` to use a different window size.

## Connection file discovery

Proton stores each game's Windows user data in a per-game prefix, as described in
the [Valve Proton FAQ](https://github.com/ValveSoftware/Proton/wiki/Proton-FAQ#how-does-proton-manage-wine-prefixes).
For the default Steam library, the bridge file is:

```text
~/.local/share/Steam/steamapps/compatdata/4348910/pfx/drive_c/users/steamuser/AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/probably-stolen-mcp/connection.json
```

The server checks Steam under `$XDG_DATA_HOME/Steam` (default `~/.local/share/Steam`),
`~/.steam/steam`, `~/.steam/root`, and
`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`. It also reads these roots'
`steamapps/libraryfolders.vdf` to find additional libraries. Paths containing spaces
and non-ASCII characters are supported; aliases to the same directory are deduplicated.
It checks only this game's prefix, without scanning unrelated game data.

Precedence is: `--connection`, `PSMCP_CONNECTION_FILE`, `STEAM_COMPAT_DATA_PATH/pfx`,
`WINEPREFIX`, then discovered Steam libraries. Prefix variables refer to directories;
the first two options refer to the actual JSON file. Automatic prefix lookup uses
Proton's `steamuser` account. For another Wine username, use an explicit file.
If multiple distinct connection files exist, the server asks for an explicit path
instead of choosing an installation. A custom prefix never falls back to another one.

To pin a connection file in the generated configuration:

```bash
python3 scripts/Configure-Mcp.py --connection '/absolute/path/to/connection.json'
```

The generator also preserves `PSMCP_CONNECTION_FILE`, or the active prefix/XDG
environment variables, when set in its shell. It stores paths, not the control token.
The AI client must have filesystem access to the prefix and network access to
`127.0.0.1`. Flatpak path discovery alone does not establish sandbox access or
Flatpak gameplay compatibility. Use an explicit accessible file if discovery fails.
Keep `connection.json` private. The bridge continues to bind only to IPv4 loopback;
no firewall opening or public listening address is needed.

## Build and test on Linux

Install `uv`, Python 3.11+, Bash, and the .NET 10 SDK (see `global.json`), then run:

```bash
bash scripts/Test.sh
bash scripts/Build.sh --game-path "$HOME/.local/share/Steam/steamapps/common/Probably Stolen"
```

For another library, pass its game directory or set `PSMCP_GAME_PATH`. The build
uses local game DLLs as references without redistributing them. It runs the Python
and C# integration suites, packages the three Mod files into
`artifacts/mod/probably-stolen-mcp`, builds the Python wheel under `artifacts/python`,
and generates the MCP client configuration.

`Build.sh` accepts `--no-restore` and `--skip-tests`. `Test.sh` accepts `--no-restore`,
`--offline` (no loopback/subprocess integration checks), or `--help-only` (stdio help
checks without .NET). Initial dependency setup still requires internet access,
including when `--offline` selects the offline test subset.
CI runs the protocol and standalone wheel checks on Windows and Ubuntu; it cannot
compile the game-dependent Mod or verify actual Proton gameplay without the game.
