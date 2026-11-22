#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/Environment.sh"
game_path="${PSMCP_GAME_PATH:-}"
no_restore=false
skip_tests=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --game-path)
            [[ $# -ge 2 ]] || { echo '--game-path requires a directory.' >&2; exit 1; }
            game_path="$2"; shift 2 ;;
        --no-restore) no_restore=true; shift ;;
        --skip-tests) skip_tests=true; shift ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
if [[ ! -f "$game_path/Probably Stolen_Data/Managed/Assembly-CSharp.dll" ]]; then
    echo 'Pass --game-path or set PSMCP_GAME_PATH to the Steam game directory containing Probably Stolen.exe.' >&2
    exit 1
fi
game_path="$(cd -- "$game_path" && pwd)"
initialize_python
initialize_dotnet
if ! "$no_restore"; then dotnet restore PSMcp.slnx --locked-mode; fi
dotnet build PSMcp.slnx -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false "-p:GamePath=$game_path"
if ! "$skip_tests"; then
    "$python" -m pytest
    dotnet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll "$python"
fi
mod_output="$repo_root/artifacts/mod/probably-stolen-mcp"
mkdir -p -- "$mod_output"
shopt -s nullglob dotglob
for path in "$mod_output"/*; do
    case "${path##*/}" in
        manifest.xml|PSMcp.Mod.dll|PSMcp.Contracts.dll) ;;
        *) echo "Unexpected file in Mod package: $path" >&2; exit 1 ;;
    esac
done
for name in manifest.xml PSMcp.Mod.dll PSMcp.Contracts.dll; do
    cp -- "$repo_root/src/PSMcp.Mod/bin/Release/netstandard2.1/$name" "$mod_output/$name"
done
uv build --wheel --out-dir "$repo_root/artifacts/python"
"$python" scripts/Configure-Mcp.py
echo "Mod package: $mod_output"
