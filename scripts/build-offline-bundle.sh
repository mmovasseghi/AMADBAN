#!/usr/bin/env bash
# Build Windows offline install folder from Linux/macOS (no internet on target server).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-$(date +%Y%m%d-%H%M)}"
OUT="$ROOT/dist/Amadban-Offline-$VERSION"
APP="$OUT/app"
INSTALL="$OUT/install"

mkdir -p "$APP" "$INSTALL" "$OUT/postgres"

echo "==> dotnet publish self-contained win-x64 -> $APP"
dotnet publish "$ROOT/Amadban.csproj" -c Release -r win-x64 --self-contained true \
  -p:PublishReadyToRun=true -o "$APP" --nologo

cp -a "$ROOT/scripts/windows/offline/." "$INSTALL/"

cat > "$OUT/نصب-آمادبان.bat" <<'BAT'
@echo off
chcp 65001 >nul
title نصب آمادبان
echo.
echo   نصب آمادبان — بدون اینترنت روی سرور
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install\Install-OneClick.ps1" -BundleRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo نصب ناموفق.
  pause
  exit /b 1
)
echo.
pause
BAT

cp "$ROOT/bundle/README.md" "$OUT/README.txt" 2>/dev/null || true
echo "Copy postgresql-*-windows-x64.exe into $OUT/postgres/" > "$OUT/postgres/README-POSTGRES.txt"
echo ""
echo "Bundle: $OUT"
echo "Add PostgreSQL installer to postgres/, then USB -> server -> نصب-آمادبان.bat (Admin)"