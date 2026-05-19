#!/usr/bin/env bash
# Shim into scripts/run.py — see that file for the actual logic.
exec python3 "$(dirname "$0")/run.py" launch-game "$@"
