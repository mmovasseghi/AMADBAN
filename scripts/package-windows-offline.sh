#!/usr/bin/env bash
# Build a flash-ready Amadban offline folder (app + postgres + vcredist + one-click install).
# CI uses this; run locally on a machine with internet to mirror the GitHub Release layout.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || date +%Y%m%d)}"
OUT="${OUT:-$ROOT/dist/Amadban-Offline-$VERSION}"
POSTGRES_URL="${POSTGRES_URL:-https://get.enterprisedb.com/postgresql/postgresql-17.4-1-windows-x64.exe}"
VCREDIST_URL="${VCREDIST_URL:-https://aka.ms/vs/17/release/vc_redist.x64.exe}"

APP="$OUT/app"
INSTALL="$OUT/install"
PG="$OUT/postgres"
PG_VC="$PG/vcredist"

mkdir -p "$APP" "$INSTALL" "$PG" "$PG_VC"

echo "==> publish self-contained win-x64"
dotnet publish "$ROOT/Amadban.csproj" -c Release -r win-x64 --self-contained true \
  -p:PublishReadyToRun=true -o "$APP" --nologo

cp -a "$ROOT/scripts/windows/offline/." "$INSTALL/"
cp "$ROOT/bundle/README.md" "$OUT/README.txt" 2>/dev/null || true

echo "==> download PostgreSQL installer (for flash; server never uses internet)"
PG_EXE="$PG/postgresql-17.4-1-windows-x64.exe"
if [[ ! -f "$PG_EXE" ]]; then
  curl -fsSL --retry 3 --retry-delay 5 -o "$PG_EXE" "$POSTGRES_URL"
fi

echo "==> download VC++ runtime (offline install on old Server 2019)"
VC_EXE="$PG_VC/VC_redist.x64.exe"
if [[ ! -f "$VC_EXE" ]]; then
  curl -fsSL --retry 3 --retry-delay 5 -o "$VC_EXE" "$VCREDIST_URL"
fi

cp "$ROOT/bundle/INSTALL.bat" "$OUT/INSTALL.bat"
# UTF-8 Persian .bat breaks cmd.exe on Server 2019; thin wrapper calls ASCII INSTALL.bat
printf '%s\r\n' '@echo off' 'call "%~dp0INSTALL.bat"' > "$OUT/نصب-آمادبان.bat"

echo "$VERSION" > "$OUT/VERSION.txt"
MANIFEST="$OUT/flash-bundle.manifest.txt"
{
  echo "Amadban offline flash bundle"
  echo "version=$VERSION"
  echo "built=$(date -u +%Y-%m-%dT%H:%MZ)"
  echo "files:"
  echo "  INSTALL.bat"
  echo "  نصب-آمادبان.bat"
  echo "  app/Amadban.exe"
  echo "  install/Install-OneClick.ps1"
  echo "  postgres/postgresql-17.4-1-windows-x64.exe"
  echo "  postgres/vcredist/VC_redist.x64.exe"
} > "$MANIFEST"

ZIP="$ROOT/dist/Amadban-Offline-Windows.zip"
rm -f "$ZIP"
(cd "$ROOT/dist" && zip -qr "$(basename "$ZIP")" "$(basename "$OUT")")
echo "Bundle: $OUT"
echo "ZIP (for GitHub / flash): $ZIP"
ls -lah "$ZIP"