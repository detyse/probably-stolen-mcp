#!/usr/bin/env python3
"""Generate local stdio MCP configuration without PowerShell or dependencies."""

import argparse
import json
import os
import shutil
import sys
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--connection", help="Explicit Linux/Windows path to the Mod's connection.json")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    uv = shutil.which("uv")
    if not uv:
        parser.error("Install uv and add it to PATH, then run this script again. See README.md.")
    if not all((root / name).is_file() for name in ("pyproject.toml", "uv.lock")):
        parser.error("Keep this script in the scripts directory of the source checkout.")
    command_args = ["--directory", str(root), "run", "--locked", "--no-dev", "probably-stolen-mcp"]
    connection = args.connection or os.environ.get("PSMCP_CONNECTION_FILE")
    if connection:
        command_args += ["--connection", str(Path(connection).expanduser().resolve())]
    entry = {"command": str(Path(uv).absolute()), "args": command_args}
    # Preserve a deliberately selected prefix for clients launched outside this shell.
    if not connection:
        env = {
            key: os.environ[key]
            for key in ("STEAM_COMPAT_DATA_PATH", "WINEPREFIX", "XDG_DATA_HOME")
            if os.environ.get(key)
        }
        if env:
            entry["env"] = env
    output = root / "artifacts/mcp-config.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"mcpServers": {"probably-stolen-mcp": entry}}, indent=2) + "\n", encoding="utf-8")
    # Redirected Windows output may use a legacy encoding that cannot show this path.
    sys.stdout.reconfigure(errors="backslashreplace")
    print(f"MCP client config: {output}")


if __name__ == "__main__":
    main()
