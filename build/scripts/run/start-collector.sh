#!/usr/bin/env bash
# =============================================================================
# start-collector.sh
# Meridian – Canonical Startup Execution Plan (Linux/macOS)
#
# Features:
#   - Builds (optional) with or without IBAPI
#   - Starts Collector
#   - Writes PID files into ./run/
#   - Handles Ctrl+C / SIGTERM gracefully (stops child processes)
#
# Usage:
#   ./start-collector.sh
#
# Flags (env vars):
#   USE_IBAPI=true|false
#   BUILD=true|false
#   DOTNET_CONFIGURATION=Release|Debug
#   IB_HOST, IB_PORT, IB_CLIENT_ID
# =============================================================================
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$ROOT_DIR/src/Meridian"
DATA_DIR="$ROOT_DIR/data"
LOG_DIR="$ROOT_DIR/logs"
RUN_DIR="$ROOT_DIR/run"

DOTNET_CONFIGURATION="${DOTNET_CONFIGURATION:-Release}"
DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Production}"

USE_IBAPI="${USE_IBAPI:-false}"
BUILD="${BUILD:-true}"

IB_HOST="${IB_HOST:-127.0.0.1}"
IB_PORT="${IB_PORT:-}"
IB_CLIENT_ID="${IB_CLIENT_ID:-17}"

mkdir -p "$DATA_DIR" "$LOG_DIR" "$RUN_DIR"

autodetect_port() {
  local host="$1"
  # Prefer explicitly provided IB_PORT
  if [[ -n "${IB_PORT:-}" ]]; then
    echo "$IB_PORT"
    return 0
  fi
  # Try common ports: 7497 (TWS paper), 7496 (TWS live), 4002 (Gateway paper), 4001 (Gateway live)
  local ports=(7497 4002 7496 4001)
  for p in "${ports[@]}"; do
    if timeout 1 bash -c ">/dev/tcp/${host}/${p}" 2>/dev/null; then
      echo "$p"
      return 0
    fi
  done
  echo ""
  return 1
}

check_port_open() {
  local host="$1"
  local port="$2"
  if timeout 1 bash -c ">/dev/tcp/${host}/${port}" 2>/dev/null; then
    return 0
  fi
  return 1
}

preflight() {
  echo "-----------------------------------------------"
  echo "[PREFLIGHT] Running checks..."
  local ok=true

  # 1) Disk space (require at least 2 GB free at root dir filesystem)
  local avail_kb
  avail_kb="$(df -Pk "$ROOT_DIR" | awk 'NR==2 {print $4}')"
  if [[ -n "$avail_kb" ]]; then
    local avail_mb=$((avail_kb/1024))
    if (( avail_mb < 2048 )); then
      echo "[WARN] Low disk space: ${avail_mb}MB free (recommend >= 2048MB)"
    else
      echo "[OK] Disk space: ${avail_mb}MB free"
    fi
  else
    echo "[WARN] Could not determine disk space (df unavailable?)"
  fi

  # 2) Permissions: data/log/run writable
  for d in "$DATA_DIR" "$LOG_DIR" "$RUN_DIR"; do
    if [[ -w "$d" ]]; then
      echo "[OK] Writable: $d"
    else
      echo "[ERROR] Not writable: $d"
      ok=false
    fi
  done

  # 3) Entitlements sanity (cannot verify entitlements without connecting; we do a "depth/trades enabled" config sanity check)
  if [[ -f "$ROOT_DIR/appsettings.json" ]]; then
    if command -v python3 >/dev/null 2>&1; then
      python3 - <<'PY' || true
import json,sys
p="appsettings.json"
try:
  cfg=json.load(open(p,"r"))
  syms=cfg.get("Symbols",[]) or cfg.get("symbols",[])
  depth=sum(1 for s in syms if s.get("SubscribeDepth") or s.get("subscribeDepth"))
  trades=sum(1 for s in syms if s.get("SubscribeTrades") or s.get("subscribeTrades"))
  print(f"[OK] Config symbols={len(syms)} depth_enabled={depth} trades_enabled={trades}")
  if depth>0:
    print("[NOTE] L2 depth requires provider depth subscription for venues.")
except Exception as e:
  print(f"[WARN] Config parse failed: {e}")
PY
    else
      echo "[WARN] python3 not available; skipping config sanity check."
    fi
  else
    echo "[WARN] appsettings.json not found in repo root."
  fi

  # 4) IB reachability (only if USE_IBAPI=true)
  if [[ "$USE_IBAPI" = true ]]; then
    if [[ -z "${IB_PORT:-}" ]]; then
      IB_PORT="$(autodetect_port "$IB_HOST" || true)"
    fi
    if [[ -z "${IB_PORT:-}" ]]; then
      echo "[ERROR] Could not autodetect IB port on host $IB_HOST (tried 7497/4002/7496/4001)."
      ok=false
    else
      echo "[OK] Detected IB port: $IB_PORT"
      if check_port_open "$IB_HOST" "$IB_PORT"; then
        echo "[OK] IB reachable at $IB_HOST:$IB_PORT"
      else
        echo "[ERROR] IB not reachable at $IB_HOST:$IB_PORT"
        ok=false
      fi
    fi
  else
    echo "[OK] IBAPI disabled; skipping IB connectivity check."
  fi

  if [[ "$ok" = false ]]; then
    echo "[PREFLIGHT] FAILED. Fix errors above and re-run."
    return 1
  fi
  echo "[PREFLIGHT] PASSED."
  echo "-----------------------------------------------"
  return 0
}


COLLECTOR_PID_FILE="$RUN_DIR/collector.pid"

echo "==============================================="
echo " MERIDIAN – STARTUP"
echo "==============================================="
echo "[INFO] Root: $ROOT_DIR"
echo "[INFO] Data: $DATA_DIR"
echo "[INFO] Logs: $LOG_DIR"
echo "[INFO] Run : $RUN_DIR"
echo "[INFO] IBAPI: $USE_IBAPI"
echo "-----------------------------------------------"

cleanup() {
  echo ""
  echo "[INFO] Shutdown requested. Stopping processes..."

  # Stop Collector
  if [[ -f "$COLLECTOR_PID_FILE" ]]; then
    COL_PID="$(cat "$COLLECTOR_PID_FILE" || true)"
    if [[ -n "${COL_PID:-}" ]] && kill -0 "$COL_PID" 2>/dev/null; then
      echo "[INFO] Sending SIGTERM to Collector ($COL_PID)"
      kill -TERM "$COL_PID" 2>/dev/null || true
    fi
  fi

  # Wait up to 10 seconds
  for i in {1..10}; do
    local alive=false
    if [[ -f "$COLLECTOR_PID_FILE" ]]; then
      COL_PID="$(cat "$COLLECTOR_PID_FILE" || true)"
      if [[ -n "${COL_PID:-}" ]] && kill -0 "$COL_PID" 2>/dev/null; then alive=true; fi
    fi
    if [[ "$alive" = false ]]; then
      break
    fi
    sleep 1
  done

  # Hard kill remaining
  if [[ -f "$COLLECTOR_PID_FILE" ]]; then
    COL_PID="$(cat "$COLLECTOR_PID_FILE" || true)"
    if [[ -n "${COL_PID:-}" ]] && kill -0 "$COL_PID" 2>/dev/null; then
      echo "[WARN] Collector still running; sending SIGKILL ($COL_PID)"
      kill -KILL "$COL_PID" 2>/dev/null || true
    fi
  fi

  rm -f "$COLLECTOR_PID_FILE" 2>/dev/null || true
  echo "[INFO] Shutdown complete."
}

trap cleanup INT TERM

cd "$ROOT_DIR"

preflight

if [[ "$BUILD" = true ]]; then
  echo "[INFO] Building..."
  if [[ "$USE_IBAPI" = true ]]; then
    dotnet build -p:DefineConstants=IBAPI -c "$DOTNET_CONFIGURATION" > "$LOG_DIR/build.log" 2>&1
  else
    dotnet build -c "$DOTNET_CONFIGURATION" > "$LOG_DIR/build.log" 2>&1
  fi
fi

echo "[INFO] Starting Collector..."
export DOTNET_ENVIRONMENT

if [[ "$USE_IBAPI" = true ]]; then
  export IB_HOST IB_PORT IB_CLIENT_ID
fi

dotnet run --project "$SRC_DIR/Meridian.csproj" --configuration "$DOTNET_CONFIGURATION" -- --watch-config --http-port 8080 > "$LOG_DIR/collector.log" 2>&1 &

COLLECTOR_PID=$!
echo "$COLLECTOR_PID" > "$COLLECTOR_PID_FILE"
echo "[INFO] Collector PID: $COLLECTOR_PID"

echo "-----------------------------------------------"
echo "[INFO] Running."
echo "[INFO] Status file: $DATA_DIR/_status/status.json"
echo "[INFO] Logs: $LOG_DIR"
echo "[INFO] Stop: Ctrl+C (graceful) or run stop-collector.sh."
echo "==============================================="

# Wait for collector (primary)
wait "$COLLECTOR_PID"
cleanup
