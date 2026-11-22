#!/usr/bin/env bash
# Shared setup for the Bash build/test entry points. Source this file.
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd -- "$repo_root"
command -v uv >/dev/null || { echo 'Install uv and add it to PATH.' >&2; exit 1; }
export UV_CACHE_DIR="$repo_root/.cache/uv"

initialize_python() {
    uv sync --locked
    python="$repo_root/.venv/bin/python"
}

initialize_dotnet() {
    command -v dotnet >/dev/null || { echo 'Install .NET 10 SDK and add it to PATH.' >&2; exit 1; }
    export DOTNET_CLI_HOME="$repo_root/.cache/dotnet"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
    export DOTNET_NOLOGO=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
    export NUGET_PACKAGES="$repo_root/.cache/nuget"
    export DOTNET_HOST_PATH="$(command -v dotnet)"
    export MSBuildEnableWorkloadResolver=false
}
