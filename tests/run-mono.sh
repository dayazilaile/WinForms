#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
setup=/workspace/cloud-setup
export MONO_PATH="$setup/mono/usr/lib/mono/4.5"
export MONO_CFG_DIR="$setup/mono/etc"
export MONO_GAC_PREFIX="$setup/mono/usr"
mono=("$setup/mono/usr/bin/mono-sgen" --config "$setup/mono/etc/mono/config")
bash "$setup/build.sh"
for name in pci.v052.ids usb.v052.ids board_models_v052.tsv component_models_v052.tsv SOURCES-v052.md; do
  if [ ! -e "$repo/build/Database/$name" ]; then cp "$repo/Database/$name" "$repo/build/Database/$name"; fi
done
for folder in Licenses Sources; do
  mkdir -p "$repo/build/Database/$folder"
  for file in "$repo/Database/$folder/"*; do
    if [ ! -e "$repo/build/Database/$folder/$(basename "$file")" ]; then cp "$file" "$repo/build/Database/$folder/"; fi
  done
done

"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/FeatureTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll "$repo/tests/FeatureTests.cs"
"${mono[@]}" "$repo/build/FeatureTests.exe"

"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/NetworkToolsTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll -r:System.Xml.dll "$repo/tests/NetworkToolsTests.cs"
"${mono[@]}" "$repo/build/NetworkToolsTests.exe"

"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/WorkspaceTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll "$repo/tests/WorkspaceTests.cs"
"${mono[@]}" "$repo/build/WorkspaceTests.exe"

"${mono[@]}" "$MONO_PATH/mcs.exe" -out:"$repo/build/TrialTests.exe" -r:"$repo/build/ITDesktopToolkit.exe" -r:System.Core.dll "$repo/tests/TrialTests.cs"
"${mono[@]}" "$repo/build/TrialTests.exe"
