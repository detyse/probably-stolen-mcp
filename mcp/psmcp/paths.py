"""Locate the Windows game bridge from a Linux Steam/Proton host."""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path

STEAM_APP_ID = "4348910"
MOD_ID = "probably-stolen-mcp"
WINDOWS_DATA = Path("AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData") / MOD_ID / "connection.json"


def _unique(paths: list[Path]) -> list[Path]:
    return list(dict.fromkeys(path.expanduser().resolve() for path in paths))


def steam_libraries() -> list[Path]:
    """Read known Steam roots and their configured libraries, without scanning disks."""
    home = Path.home()
    data_home = Path(os.environ.get("XDG_DATA_HOME") or home / ".local/share")
    roots = _unique(
        [
            data_home / "Steam",
            home / ".steam/steam",
            home / ".steam/root",
            home / ".var/app/com.valvesoftware.Steam/.local/share/Steam",
        ]
    )
    libraries = list(roots)
    for root in roots:
        try:
            text = (root / "steamapps/libraryfolders.vdf").read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            continue
        # Only modern library "path" values are needed. Do not interpret Unicode
        # paths as Python escapes (Steam libraries may contain non-ASCII names).
        for value in re.findall(r'^\s*"path"\s+"((?:\\.|[^"\\])*)"', text, re.MULTILINE):
            path = Path(re.sub(r'\\([\\"])', r"\1", value))
            if path.is_absolute():
                libraries.append(path)
    return _unique(libraries)


def _prefix_connection(prefix: Path) -> Path:
    return prefix / "drive_c/users/steamuser" / WINDOWS_DATA


def default_connection_path() -> Path:
    if sys.platform != "linux":
        return Path.home() / WINDOWS_DATA
    # An explicitly selected Proton prefix must not silently fall back to a
    # different installation. --connection remains the escape hatch for Wine
    # installations using a username other than Proton's steamuser.
    compat_data = os.environ.get("STEAM_COMPAT_DATA_PATH")
    if compat_data:
        return _prefix_connection(Path(compat_data).expanduser() / "pfx")
    wine_prefix = os.environ.get("WINEPREFIX")
    if wine_prefix:
        return _prefix_connection(Path(wine_prefix).expanduser())
    libraries = steam_libraries()
    prefixes = [library / "steamapps/compatdata" / STEAM_APP_ID / "pfx" for library in libraries]
    connections = [_prefix_connection(prefix) for prefix in prefixes]
    existing = [path for path in connections if path.is_file()]
    if len(existing) > 1:
        raise ValueError(
            "Multiple Proton connection files found. Select one with --connection or PSMCP_CONNECTION_FILE."
        )
    if existing:
        return existing[0]
    # Resolve the right location even before the Mod writes its first file.
    for prefix, connection in zip(prefixes, connections, strict=True):
        if prefix.is_dir():
            return connection
    for library, connection in zip(libraries, connections, strict=True):
        if (library / f"steamapps/appmanifest_{STEAM_APP_ID}.acf").is_file():
            return connection
    return connections[0]
