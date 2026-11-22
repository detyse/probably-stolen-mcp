#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/Environment.sh"
no_restore=false
mode=full
for arg in "$@"; do
    case "$arg" in
        --no-restore) no_restore=true ;;
        --offline|--help-only)
            [[ "$mode" == full ]] || { echo 'Choose either --offline or --help-only.' >&2; exit 1; }
            mode="${arg#--}" ;;
        *) echo "Unknown option: $arg" >&2; exit 1 ;;
    esac
done
initialize_python
if [[ "$mode" == help-only ]]; then
    "$python" -m pytest tests/python/test_stdio.py
    exit
fi
if [[ "$mode" == offline ]]; then
    "$python" -m pytest -m 'not integration'
else
    "$python" -m pytest
fi
initialize_dotnet
if ! "$no_restore"; then
    dotnet restore tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj --locked-mode
fi
dotnet build tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
if [[ "$mode" == offline ]]; then
    dotnet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll --offline
else
    dotnet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll "$python"
fi
