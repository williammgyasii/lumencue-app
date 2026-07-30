#!/usr/bin/env bash
# Publishes a self-contained osx-arm64 LumenCue build, packs Velopack artifacts, and uploads
# to the public lumencue-releases repo. Usage:
#   installer/build-release-mac.sh 0.7.23
set -euo pipefail

VERSION="${1:?Usage: build-release-mac.sh <version>}"
TOKEN="${2:-$(gh auth token)}"
REPO_URL="${REPO_URL:-https://github.com/williammgyasii/lumencue-releases}"
RUNTIME="${RUNTIME:-osx-arm64}"
SKIP_UPLOAD="${SKIP_UPLOAD:-0}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJ="$REPO_ROOT/src/ChurchProjection.App/ChurchProjection.App.csproj"
APP_OUT="$REPO_ROOT/publish/app-mac"
RELEASE_DIR="$REPO_ROOT/publish/releases-mac"
ICON="$REPO_ROOT/src/ChurchProjection.UI/Assets/app-icon.png"
PACK_ID="LumenCue"
MAIN_EXE="ChurchProjection.App"

echo "==> Publishing self-contained $RUNTIME build (v$VERSION)..."
rm -rf "$APP_OUT"
dotnet publish "$PROJ" -c Release -r "$RUNTIME" --self-contained true \
  -p:Version="$VERSION" -o "$APP_OUT"

LOCAL_CFG="$APP_OUT/appsettings.local.json"
if [[ -f "$LOCAL_CFG" ]]; then
  echo "ERROR: appsettings.local.json found in published build — secrets must never ship." >&2
  exit 1
fi
echo "Verified: no appsettings.local.json in build."

if ! command -v vpk >/dev/null 2>&1; then
  echo "==> Installing Velopack CLI (vpk)..."
  dotnet tool install --global vpk
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

echo "==> Packing Velopack release v$VERSION..."
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"
PACK_ARGS=(
  pack
  --packId "$PACK_ID"
  --packVersion "$VERSION"
  --packDir "$APP_OUT"
  --mainExe "$MAIN_EXE"
  --packTitle "LumenCue"
  --packAuthors "LumenCue"
  --outputDir "$RELEASE_DIR"
)
if [[ -f "$ICON" ]]; then
  PACK_ARGS+=(--icon "$ICON")
fi
vpk "${PACK_ARGS[@]}"

echo "==> Release artifacts ready in: $RELEASE_DIR"

if [[ "$SKIP_UPLOAD" == "1" ]]; then
  echo "Skipping upload (SKIP_UPLOAD=1)."
  exit 0
fi

echo "==> Uploading release v$VERSION to $REPO_URL..."
vpk upload github \
  --repoUrl "$REPO_URL" \
  --token "$TOKEN" \
  --outputDir "$RELEASE_DIR" \
  --releaseName "LumenCue $VERSION" \
  --tag "v$VERSION" \
  --publish

echo "==> Released v$VERSION for Mac."
