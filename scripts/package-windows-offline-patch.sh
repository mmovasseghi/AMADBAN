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
پچ نصب آمادبان ${PATCH_VER} — فقط چند KB (نه 400MB)
=====================================================

برای کسانی که Amadban-Offline-Windows.zip نسخه v1.0.0 را دارند.

جایگزینی (Replace) روی ویندوز:
1. پوشه فلش را باز کن: داخلش app و install و postgres باشد.
2. فایل Amadban-Offline-Patch-${PATCH_VER}.zip را کپی کن داخل همان پوشه.
3. راست کلیک روی ZIP → Extract All → Extract
   (یا WinRAR: Extract here — وقتی پرسید Replace file? بزن Yes to All)
4. INSTALL.bat → Run as administrator

فقط این فایل‌ها عوض می‌شوند:
  INSTALL.bat
  نصب-آمادبان.bat
  install/Run-Install.ps1
  install/Install-OneClick.ps1

app/ و postgres/ دست نخورده می‌مانند.

built=${STAMP}
applies_to=offline-v1.0.0_and_v1.0.1_flash_folder
EOF

echo "patch=${PATCH_VER}" > "$OUT/PATCH-VERSION.txt"
echo "applies_to=offline-v1.0.0" >> "$OUT/PATCH-VERSION.txt"
echo "built=$(date -u +%Y-%m-%dT%H:%MZ)" >> "$OUT/PATCH-VERSION.txt"

rm -f "$ZIP"
# ZIP root = INSTALL.bat, install/, ... (extract inside flash folder → replace in place)
(cd "$OUT" && zip -qr "$ZIP" .)
ls -lah "$ZIP"
echo "Patch folder: $OUT"
echo "Patch ZIP: $ZIP"
wc -c "$ZIP"