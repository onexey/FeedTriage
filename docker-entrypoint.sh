#!/bin/sh
set -eu

state_path="${FEEDTRIAGE__STATE__FILE_PATH:-./data/state.json}"
state_dir="$(dirname "$state_path")"

if [ "$(id -u)" = "0" ]; then
    mkdir -p "$state_dir"

    if [ -d "$state_dir" ] && [ "$state_dir" != "/" ]; then
        chown appuser:appgroup "$state_dir"
    fi

    if [ -e "$state_path" ] && [ "$state_path" != "/" ]; then
        chown appuser:appgroup "$state_path"
    fi

    exec runuser -u appuser -- "$@"
fi

exec "$@"
