#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
setup=/workspace/cloud-setup
export MONO_PATH="$setup/mono/usr/lib/mono/4.5"
export MONO_CFG_DIR="$setup/mono/etc"
export MONO_GAC_PREFIX="$setup/mono/usr"
mono=("$setup/mono/usr/bin/mono-sgen" --config "$setup/mono/etc/mono/config")
bash "$setup/build.sh"
"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/FeatureTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll "$repo/tests/FeatureTests.cs"
"${mono[@]}" "$repo/build/FeatureTests.exe"

"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/NetworkToolsTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll -r:System.Xml.dll "$repo/tests/NetworkToolsTests.cs"
"${mono[@]}" "$repo/build/NetworkToolsTests.exe"
