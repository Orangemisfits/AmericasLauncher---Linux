#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Builds dist/AmericasLauncher-<version>-x86_64.AppImage from src/AmericasLauncher.Linux.
#
# Needs the .NET 8 SDK and curl (appimagetool is downloaded once into dist/tools and checked
# against a pinned SHA-256). FUSE is not needed to build.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/src/AmericasLauncher.Linux/AmericasLauncher.Linux.csproj"
PACKAGING="$ROOT/packaging/linux"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n1)"
ARCH=x86_64
DIST="$ROOT/dist"
APPDIR="$DIST/AppDir"
TOOLS="$DIST/tools"
OUTPUT="$DIST/AmericasLauncher-$VERSION-$ARCH.AppImage"

APPIMAGETOOL_VERSION=1.9.1
APPIMAGETOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
APPIMAGETOOL="$TOOLS/appimagetool-$APPIMAGETOOL_VERSION-x86_64.AppImage"

echo "==> Americas Launcher $VERSION ($ARCH)"

# 1) Self-contained .NET publish into the AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/lib/americas-launcher" "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps" "$TOOLS"

dotnet publish "$PROJECT" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:DebugType=none \
    -p:GenerateDocumentationFile=false \
    -o "$APPDIR/usr/lib/americas-launcher"

# 2) AppDir metadata
install -m 755 "$PACKAGING/AppRun" "$APPDIR/AppRun"
install -m 644 "$PACKAGING/americas-launcher.desktop" "$APPDIR/americas-launcher.desktop"
install -m 644 "$PACKAGING/americas-launcher.desktop" "$APPDIR/usr/share/applications/americas-launcher.desktop"
install -m 644 "$PACKAGING/americas-launcher.png" "$APPDIR/americas-launcher.png"
install -m 644 "$PACKAGING/americas-launcher.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/americas-launcher.png"
ln -sf americas-launcher.png "$APPDIR/.DirIcon"
ln -sf ../lib/americas-launcher/AmericasLauncher "$APPDIR/usr/bin/americas-launcher"

# 3) appimagetool (pinned)
if [ ! -f "$APPIMAGETOOL" ]; then
    echo "==> Downloading appimagetool $APPIMAGETOOL_VERSION"
    curl -fsSL -o "$APPIMAGETOOL.part" \
        "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
    echo "$APPIMAGETOOL_SHA256  $APPIMAGETOOL.part" | sha256sum -c -
    mv "$APPIMAGETOOL.part" "$APPIMAGETOOL"
fi
chmod +x "$APPIMAGETOOL"

# 4) Pack. APPIMAGE_EXTRACT_AND_RUN: appimagetool itself runs without FUSE (CI, containers).
rm -f "$OUTPUT"
ARCH="$ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTPUT"
chmod +x "$OUTPUT"

echo "==> $OUTPUT"
