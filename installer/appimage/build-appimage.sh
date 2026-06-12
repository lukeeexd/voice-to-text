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

# Bundle ICU from the build host: .NET fails fast without libicu, and minimal
# systems don't ship it. Self-contained apps probe the app dir first, so the
# versioned .so files (and their symlinks, -P) just need to sit beside the exe.
for lib in /usr/lib/x86_64-linux-gnu/libicuuc.so.* \
           /usr/lib/x86_64-linux-gnu/libicui18n.so.* \
           /usr/lib/x86_64-linux-gnu/libicudata.so.*; do
  cp -P "$lib" "$APPDIR/usr/bin/"
done

# Bundle the small X11 client libs Avalonia dlopens: desktop distros all have
# them, but near-minimal systems (fresh WSL, containers) miss e.g. libICE and
# the daemon should still bring up its GUI there. libX11/fontconfig stay on the
# host per AppImage convention. (The daemon also degrades headless if the GUI
# can't start at all.)
sudo apt-get install -y --no-install-recommends \
  libice6 libsm6 libxext6 libxrender1 libxrandr2 libxi6 libxcursor1 libxfixes3 >/dev/null
for lib in libICE.so.6 libSM.so.6 libXext.so.6 libXrender.so.1 \
           libXrandr.so.2 libXi.so.6 libXcursor.so.1 libXfixes.so.3; do
  cp -P /usr/lib/x86_64-linux-gnu/"$lib"* "$APPDIR/usr/bin/" 2>/dev/null || true
done
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
