#!/usr/bin/env bash
# Run ON the test server (185.93.88.81) as root or a user with sudo.
# Updates /opt/amadban-cursor-test from GitHub main and restarts the service.
set -euo pipefail

DEPLOY_ROOT="${AMADBAN_DEPLOY_ROOT:-/opt/amadban-cursor-test}"
SERVICE="${AMADBAN_SERVICE:-amadban-cursor-test}"
REPO="${AMADBAN_REPO:-git@github.com:mmovasseghi/AMADBAN.git}"
BRANCH="${AMADBAN_BRANCH:-main}"
SRC="$DEPLOY_ROOT/src"
PUBLISH="$DEPLOY_ROOT/publish"

echo "==> Deploying $BRANCH to $DEPLOY_ROOT"

if [[ -d "$SRC/.git" ]]; then
  cd "$SRC"
  git fetch origin "$BRANCH"
  git checkout "$BRANCH"
  git pull origin "$BRANCH"
else
  mkdir -p "$DEPLOY_ROOT"
  git clone --depth 1 --branch "$BRANCH" "$REPO" "$SRC"
  cd "$SRC"
fi

echo "==> Commit: $(git rev-parse --short HEAD)"

systemctl stop "$SERVICE" || true

dotnet publish "$SRC" -c Release -o "$PUBLISH" --nologo -v minimal

systemctl start "$SERVICE"
systemctl is-active --quiet "$SERVICE"

echo "==> OK — $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8088/Login || echo 'curl failed') on /Login"