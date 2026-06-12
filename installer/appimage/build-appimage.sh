#!/usr/bin/env bash
# Builds VoiceToText-x86_64.AppImage from a linux-x64 publish directory.
# Usage: build-appimage.sh <publish-dir> [output-dir]
#
# Uses the AppImage project's appimagetool (continuous), whose embedded static
# type-2 runtime avoids the historical libfuse2 dependency on modern distros.
# --appimage-extract-and-run lets the tool itself run on FUSE-less CI runners.
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-appimage.sh <publish-dir> [output-dir]}"
OUT_DIR="${2:-.}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APPDIR="$WORK/AppDir"

mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/voicetotext"
ln -s usr/bin/voicetotext "$APPDIR/AppRun"
cp "$SCRIPT_DIR/voicetotext.desktop" "$APPDIR/"
cp "$SCRIPT_DIR/voicetotext.png" "$APPDIR/"

TOOL="$WORK/appimagetool"
curl -sL -o "$TOOL" \
  "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
chmod +x "$TOOL"

mkdir -p "$OUT_DIR"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$OUT_DIR/VoiceToText-x86_64.AppImage"
echo "Built: $OUT_DIR/VoiceToText-x86_64.AppImage"
