#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != Darwin ]]; then
  echo "Generating the macOS ICNS requires macOS sips and iconutil; no ICNS was written."
  exit 1
fi

MASTER="$ROOT/assets/branding/autoclicker-icon-1024.png"
ICONSET="$ROOT/assets/branding/autoclicker.iconset"
OUTPUT="$ROOT/assets/branding/autoclicker.icns"
if [[ ! -f "$MASTER" ]]; then
  echo "The canonical 1024px icon master is missing: $MASTER" >&2
  exit 1
fi

rm -rf "$ICONSET"
mkdir -p "$ICONSET"
render_size() {
  local size="$1" name="$2"
  sips --resampleHeightWidth "$size" "$size" "$MASTER" --out "$ICONSET/$name" >/dev/null
}

render_size 16 icon_16x16.png
render_size 32 icon_16x16@2x.png
render_size 32 icon_32x32.png
render_size 64 icon_32x32@2x.png
render_size 128 icon_128x128.png
render_size 256 icon_128x128@2x.png
render_size 256 icon_256x256.png
render_size 512 icon_256x256@2x.png
render_size 512 icon_512x512.png
render_size 1024 icon_512x512@2x.png

iconutil --convert icns --output "$OUTPUT" "$ICONSET"
rm -rf "$ICONSET"
echo "Created $OUTPUT from the canonical 1024px RGBA master using macOS iconutil."
