#!/usr/bin/env bash
# Wrapper — same output as GitHub Actions release ZIP.
exec "$(cd "$(dirname "$0")" && pwd)/package-windows-offline.sh" "${1:-$(date +%Y%m%d-%H%M)}"