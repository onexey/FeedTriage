#!/bin/sh
set -eu

raw_state_path="${FEEDTRIAGE__STATE__FILE_PATH:-/app/data/state.json}"

case "$raw_state_path" in
    /*) state_path="$(realpath -m "$raw_state_path")" ;;
    *) state_path="$(realpath -m "/app/$raw_state_path")" ;;
esac

case "$state_path" in
    /app/*|/data/*) should_prepare_state_dir="true" ;;
    *) should_prepare_state_dir="false" ;;
esac

state_dir="$(dirname "$state_path")"

if [ "$(id -u)" = "0" ]; then
    if [ "$should_prepare_state_dir" = "true" ]; then
        mkdir -p "$state_dir"
        chown appuser:appgroup "$state_dir"

        if [ -e "$state_path" ]; then
            chown appuser:appgroup "$state_path"
        fi
    fi

    exec runuser -u appuser -- "$@"
fi

exec "$@"
