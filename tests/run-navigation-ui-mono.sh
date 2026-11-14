#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
setup=/workspace/cloud-setup
export MONO_PATH="$setup/mono/usr/lib/mono/4.5"
export MONO_CFG_DIR="$setup/mono/etc"
export MONO_GAC_PREFIX="$setup/mono/usr"
export LD_LIBRARY_PATH="$setup/mono/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export XDG_DATA_HOME="$setup/ui-data"
export XDG_CACHE_HOME="$setup/ui-cache"
mkdir -p "$XDG_DATA_HOME" "$XDG_CACHE_HOME"
: "${DISPLAY:?Start a virtual display or select the existing display first}"
mono=("$setup/mono/usr/bin/mono-sgen" --config "$setup/mono/etc/mono/config")
"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/NavigationUiTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll "$repo/tests/NavigationUiTests.cs"
"${mono[@]}" "$repo/build/NavigationUiTests.exe"
