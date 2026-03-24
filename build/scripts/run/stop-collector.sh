#!/usr/bin/env bash
# stop-collector.sh - Stops Collector/UI using pid files in ./run/
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="$ROOT_DIR/run"
COLLECTOR_PID_FILE="$RUN_DIR/collector.pid"
UI_PID_FILE="$RUN_DIR/ui.pid"

stop_pid() {
  local file="$1"
  local name="$2"
  if [[ -f "$file" ]]; then
    local pid
    pid="$(cat "$file" || true)"
    if [[ -n "${pid:-}" ]] && kill -0 "$pid" 2>/dev/null; then
      echo "[INFO] Stopping $name ($pid)"
      kill -TERM "$pid" 2>/dev/null || true
    fi
  fi
}

stop_pid "$UI_PID_FILE" "UI"
stop_pid "$COLLECTOR_PID_FILE" "Collector"
echo "[INFO] Done. If processes persist, wait a few seconds or kill manually."
