#!/usr/bin/env bash
# Small patch ZIP: copy onto existing Amadban-Offline-* (e.g. from offline-v1.0.0).
# Does NOT include app/, postgres/, or full rebuild (~few KB only).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PATCH_VER="${1:-1.0.1}"
STAMP="${2:-$(date -u +%Y%m%d)}"
OUT="$ROOT/dist/Amadban-Offline-Patch-${PATCH_VER}"
ZIP="$ROOT/dist/Amadban-Offline-Patch-${PATCH_VER}.zip"

rm -rf "$OUT"
mkdir -p "$OUT/install"

cp "$ROOT/bundle/INSTALL.bat" "$OUT/INSTALL.bat"
printf '%s\r\n' '@echo off' 'call "%~dp0INSTALL.bat"' > "$OUT/نصب-آمادبان.bat"

cp "$ROOT/scripts/windows/offline/Run-Install.ps1" "$OUT/install/Run-Install.ps1"
cp "$ROOT/scripts/windows/offline/Install-OneClick.ps1" "$OUT/install/Install-OneClick.ps1"
cp "$ROOT/scripts/windows/offline/Verify-FlashBundle.ps1" "$OUT/install/Verify-FlashBundle.ps1"

cat > "$OUT/README-PATCH.fa.txt" <<EOF
پچ نصب آمادبان ${PATCH_VER} (روی بسته offline-v1.0.0)
============================================

حجم کم است — فقط فایل‌های نصب اصلاح‌شده.

روی VPS یا فلش:
1. همان پوشه Amadban-Offline-... که از v1.0.0 دارید را باز کنید.
2. فایل‌های این ZIP را Extract کنید داخل همان پوشه (جایگزین شود).
3. app و postgres را دست نزنید.
4. RUN: INSTALL.bat (Administrator) یا install\\Run-Install.ps1

فایل‌های پچ:
  INSTALL.bat
  نصب-آمادبان.bat
  install\\Run-Install.ps1
  install\\Install-OneClick.ps1

built=${STAMP}
applies_to=offline-v1.0.0
EOF

echo "patch=${PATCH_VER}" > "$OUT/PATCH-VERSION.txt"
echo "applies_to=offline-v1.0.0" >> "$OUT/PATCH-VERSION.txt"
echo "built=$(date -u +%Y-%m-%dT%H:%MZ)" >> "$OUT/PATCH-VERSION.txt"

rm -f "$ZIP"
(cd "$ROOT/dist" && zip -qr "$(basename "$ZIP")" "$(basename "$OUT")")
ls -lah "$ZIP"
echo "Patch folder: $OUT"
echo "Patch ZIP: $ZIP"
wc -c "$ZIP"