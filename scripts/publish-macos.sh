#!/usr/bin/env bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
if [[ ! -f AutoClicker.sln ]]; then echo "AutoClicker.sln not found at $ROOT" >&2; exit 1; fi
if [[ "$(uname -s)" != Darwin ]]; then
  echo "macOS publish, .app signing and DMG creation require a macOS host (hdiutil/codesign/plutil). No macOS artifact was generated."
  exit 0
fi
bash scripts/generate-macos-icons.sh

SKIP_TESTS=0
INCLUDE_X64=0
SKIP_SIGN=0
SKIP_NOTARIZE=0
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    --include-x64) INCLUDE_X64=1 ;;
    --skip-sign) SKIP_SIGN=1 ;;
    --skip-notarize) SKIP_NOTARIZE=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done
VERSION="$(python3 -c 'import pathlib,xml.etree.ElementTree as E; r=E.parse("Directory.Build.props").getroot(); print(r.findtext("./PropertyGroup/Version"))')"
BUNDLE_ID="$(python3 -c 'import pathlib,xml.etree.ElementTree as E; r=E.parse("Directory.Build.props").getroot(); print(r.findtext("./PropertyGroup/ApplicationBundleIdentifier"))')"
PUBLISH="artifacts/publish/osx-arm64"
RELEASE="artifacts/release/macos"
rm -rf "$PUBLISH" "$RELEASE/AutoClicker.app" "$RELEASE/AutoClicker-$VERSION-macOS-arm64.dmg"
mkdir -p "$RELEASE"
# Build, sign and image the bundle in a temp folder outside iCloud Drive / Documents.
# File Provider and Finder xattrs added there make codesign fail with "detritus not allowed".
WORK="$(mktemp -d)"
SMOKE_DATA=""
cleanup() { rm -rf "$WORK" "$SMOKE_DATA"; }
trap cleanup EXIT

notarize_dmg() {
  local dmg="$1" response status
  if [[ -n "${APPLE_NOTARY_PROFILE:-}" ]]; then
    response="$(xcrun notarytool submit "$dmg" --keychain-profile "$APPLE_NOTARY_PROFILE" --wait --output-format json)"
  elif [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
    response="$(xcrun notarytool submit "$dmg" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait --output-format json)"
  else
    echo "Notarization skipped: no notarytool keychain profile or complete Apple credentials configured."
    return 0
  fi
  printf '%s\n' "$response"
  status="$(printf '%s' "$response" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("status", ""))')"
  if [[ "$status" != Accepted ]]; then echo "Notarization did not return Accepted status: $status" >&2; return 1; fi
  xcrun stapler staple "$dmg"
  xcrun stapler validate "$dmg"
}

dotnet restore AutoClicker.sln -m:1
if [[ "$SKIP_TESTS" == 0 ]]; then
  dotnet build AutoClicker.sln -c Debug -m:1 --no-restore
  dotnet test AutoClicker.sln -c Debug -m:1 --no-build
  dotnet build AutoClicker.sln -c Release -m:1 --no-restore
  dotnet test AutoClicker.sln -c Release -m:1 --no-build
fi
dotnet publish AutoClicker.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o "$PUBLISH"
APP="$WORK/AutoClicker.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH"/. "$APP/Contents/MacOS/"
cp assets/branding/autoclicker.icns "$APP/Contents/Resources/autoclicker.icns"
sed -e "s|@BUNDLE_ID@|$BUNDLE_ID|g" -e "s|@VERSION@|$VERSION|g" packaging/macos/Info.plist.in > "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/AutoClicker"
plutil -lint "$APP/Contents/Info.plist"
xattr -cr "$APP"
SMOKE_DATA="$(mktemp -d)"
"$APP/Contents/MacOS/AutoClicker" --smoke-test "--data-dir=$SMOKE_DATA"
rm -rf "$SMOKE_DATA"
SMOKE_DATA=""

SIGNED=0
if [[ "$SKIP_SIGN" == 0 && -n "${APPLE_SIGNING_IDENTITY:-}" ]]; then
  codesign --force --deep --options runtime --timestamp --sign "$APPLE_SIGNING_IDENTITY" "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
  SIGNED=1
elif [[ "$SKIP_SIGN" == 0 ]] && security find-identity -p codesigning | grep -q "AutoClicker Local Signing"; then
  # Free local certificate (see README): a stable identity keeps Accessibility / Input Monitoring grants across builds.
  codesign --force --deep --sign "AutoClicker Local Signing" "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
  echo 'Signed with the local "AutoClicker Local Signing" certificate.'
else
  # Free ad-hoc signature: gives macOS a consistent app identity so Accessibility / Input Monitoring grants stick across builds.
  codesign --force --deep --sign - "$APP"
  codesign --force -r='designated => identifier "'"$BUNDLE_ID"'"' --sign - "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
  echo 'Ad-hoc signed build with stable designated requirement.'
fi

DMG="$RELEASE/AutoClicker-$VERSION-macOS-arm64.dmg"
STAGING="$WORK/dmg-staging"
rm -rf "$STAGING"
mkdir -p "$STAGING"
cp -R "$APP" "$STAGING/AutoClicker.app"
ln -s /Applications "$STAGING/Applications"
hdiutil create -volname AutoClicker -srcfolder "$STAGING" -ov -format UDZO "$DMG"
rm -rf "$STAGING"

# The signed .app is intentionally not copied into the iCloud-synced folder: iCloud re-adds
# xattrs that break the signature. Ship the DMG; drag the app from it into /Applications.

if [[ "$SIGNED" == 1 && "$SKIP_NOTARIZE" == 0 ]]; then
  notarize_dmg "$DMG"
else
  echo 'Notarization skipped: the app is unsigned or --skip-notarize was specified.'
fi

shasum -a 256 "$DMG" | tee "$DMG.sha256"
echo "App bundle is inside the DMG as AutoClicker.app (not kept in the iCloud folder)"
echo "DMG: $DMG"
echo "SHA-256: $DMG.sha256"

if [[ "$INCLUDE_X64" == 1 ]]; then
  X64_PUBLISH="artifacts/publish/osx-x64"
  X64_APP="$WORK/AutoClicker-x64.app"
  X64_DMG="$RELEASE/AutoClicker-$VERSION-macOS-x64.dmg"
  rm -rf "$X64_PUBLISH" "$X64_APP" "$X64_DMG"
  dotnet publish AutoClicker.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o "$X64_PUBLISH"
  mkdir -p "$X64_APP/Contents/MacOS" "$X64_APP/Contents/Resources"
  cp -R "$X64_PUBLISH"/. "$X64_APP/Contents/MacOS/"
  cp assets/branding/autoclicker.icns "$X64_APP/Contents/Resources/autoclicker.icns"
  sed -e "s|@BUNDLE_ID@|$BUNDLE_ID|g" -e "s|@VERSION@|$VERSION|g" packaging/macos/Info.plist.in > "$X64_APP/Contents/Info.plist"
  chmod +x "$X64_APP/Contents/MacOS/AutoClicker"
  plutil -lint "$X64_APP/Contents/Info.plist"
  xattr -cr "$X64_APP"
  if [[ "$SIGNED" == 1 ]]; then
    codesign --force --deep --options runtime --timestamp --sign "$APPLE_SIGNING_IDENTITY" "$X64_APP"
    codesign --verify --deep --strict --verbose=2 "$X64_APP"
  elif security find-identity -p codesigning | grep -q "AutoClicker Local Signing"; then
    codesign --force --deep --sign "AutoClicker Local Signing" "$X64_APP"
    codesign --verify --deep --strict --verbose=2 "$X64_APP"
  else
    codesign --force --deep --sign - "$X64_APP"
    codesign --force -r='designated => identifier "'"$BUNDLE_ID"'"' --sign - "$X64_APP"
    codesign --verify --deep --strict --verbose=2 "$X64_APP"
  fi
  X64_STAGE="$WORK/dmg-staging-x64"
  rm -rf "$X64_STAGE"
  mkdir -p "$X64_STAGE"
  cp -R "$X64_APP" "$X64_STAGE/AutoClicker.app"
  ln -s /Applications "$X64_STAGE/Applications"
  hdiutil create -volname AutoClicker -srcfolder "$X64_STAGE" -ov -format UDZO "$X64_DMG"
  rm -rf "$X64_STAGE"
  if [[ "$SIGNED" == 1 && "$SKIP_NOTARIZE" == 0 ]]; then
    notarize_dmg "$X64_DMG"
  fi
  shasum -a 256 "$X64_DMG" | tee "$X64_DMG.sha256"

  echo "x64 DMG: $X64_DMG"
fi
