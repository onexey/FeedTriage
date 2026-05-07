#!/bin/sh
set -eu

STATE_DIR="/app/data"
STATE_FILE="$STATE_DIR/state.json"

mkdir -p "$STATE_DIR"

if [ ! -f "$STATE_FILE" ]; then
  printf '{\n  "lastPublishedAt": null\n}\n' > "$STATE_FILE"
fi

chown -R appuser:appgroup "$STATE_DIR" 2>/dev/null || true

if runuser --user appuser -- test -w "$STATE_FILE"; then
  exec runuser --user appuser -- dotnet FeedTriage.Worker.dll
fi

echo "Warning: $STATE_FILE is not writable by appuser; continuing as root." >&2
exec dotnet FeedTriage.Worker.dll
