#!/usr/bin/env bash
set -euo pipefail
command -v steam >/dev/null || { echo 'Steam was not found in PATH.' >&2; exit 1; }
# Local testing defaults to a window; append Unity arguments to override its size.
exec steam -applaunch 4348910 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 "$@"
