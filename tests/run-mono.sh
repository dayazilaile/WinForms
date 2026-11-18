#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
setup=/workspace/cloud-setup
export MONO_PATH="$setup/mono/usr/lib/mono/4.5" MONO_CFG_DIR="$setup/mono/etc" MONO_GAC_PREFIX="$setup/mono/usr"
export LD_LIBRARY_PATH="$setup/mono/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
mono=("$setup/mono/usr/bin/mono-sgen" --config "$setup/mono/etc/mono/config")
mkdir -p "$repo/build"
export PYTHONPATH="/workspace/cloud-setup/metadata${PYTHONPATH:+:$PYTHONPATH}"
python3 "$repo/tools/build_framework.py" --project "$repo" --references "${NETFRAMEWORK_REFS:-/workspace/cloud-setup/netfx48}" --standalone

for name in StandaloneTests StandaloneUiTests; do
 "${mono[@]}" "$MONO_PATH/mcs.exe" -langversion:5 -out:"$repo/build/$name.exe" -r:"$repo/build/BatAutomation.exe" -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll "$repo/tests/$name.cs"
done
"${mono[@]}" "$repo/build/StandaloneTests.exe"
if [ -n "${DISPLAY:-}" ]; then
 clean=$(mktemp -d /tmp/bat-standalone-ui-XXXXXX)
 trap 'rm -rf "$clean"' EXIT
 cp "$repo/build/BatAutomation.exe" "$repo/build/StandaloneUiTests.exe" "$clean/"
 (cd "$clean"
  "${mono[@]}" ./StandaloneUiTests.exe
 )
 cp "$clean/"*.png "$repo/build/"
fi
