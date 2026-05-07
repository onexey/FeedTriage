#!/bin/sh
set -eu

STATE_DIR="/app/data"
STATE_FILE="$STATE_DIR/state.json"

mkdir -p "$STATE_DIR"

if [ ! -f "$STATE_FILE" ]; then
  printf '{\n  "lastPublishedAt": null\n}\n' > "$STATE_FILE"
fi

chown appuser:appgroup "$STATE_DIR" "$STATE_FILE" 2>/dev/null || true

if runuser --user appuser -- test -w "$STATE_FILE"; then
  exec runuser --user appuser -- dotnet FeedTriage.Worker.dll
fi

echo "Error: $STATE_FILE must be writable by appuser. Fix the mounted volume permissions for /app/data." >&2
exit 1
