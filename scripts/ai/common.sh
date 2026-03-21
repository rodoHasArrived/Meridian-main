#!/usr/bin/env bash

set -euo pipefail

AI_REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
AI_STATE_DIR="$AI_REPO_ROOT/.ai"
AI_LOG_DIR="$AI_STATE_DIR/logs"
AI_ENV_FILE="$AI_STATE_DIR/env.sh"
AI_STATUS_FILE="$AI_STATE_DIR/maintenance-status.json"
AI_ROUTE_FILE="$AI_STATE_DIR/maintenance-route.json"
AI_DOTNET_DIR="${HOME}/.dotnet"

mkdir -p "$AI_STATE_DIR" "$AI_LOG_DIR"

ai::command_exists() {
    command -v "$1" >/dev/null 2>&1
}

ai::sdk_version() {
    python3 - "$AI_REPO_ROOT/global.json" <<'PY'
import json
from pathlib import Path

data = json.loads(Path(__import__("sys").argv[1]).read_text(encoding="utf-8"))
print(data["sdk"]["version"])
PY
}

ai::write_env_file() {
    mkdir -p "$AI_DOTNET_DIR"
    cat >"$AI_ENV_FILE" <<EOF
#!/usr/bin/env bash
export DOTNET_ROOT="$AI_DOTNET_DIR"
export PATH="$AI_DOTNET_DIR:$AI_DOTNET_DIR/tools:\$PATH"
EOF
    chmod +x "$AI_ENV_FILE"
}

ai::load_env() {
    ai::write_env_file
    # shellcheck disable=SC1090
    source "$AI_ENV_FILE"
}

ai::ensure_dotnet() {
    local requested_version
    requested_version="$(ai::sdk_version)"

    ai::load_env

    if ai::command_exists dotnet; then
        return 0
    fi

    local installer="$AI_STATE_DIR/dotnet-install.sh"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    chmod +x "$installer"
    "$installer" --version "$requested_version" --install-dir "$AI_DOTNET_DIR" --no-path
    ai::load_env
    dotnet --info >/dev/null
}

ai::status_init() {
    AI_STATUS_MODE="$1"
    AI_STATUS_ROUTE="$2"
    AI_STATUS_NEXT_ACTION="${3:-Review maintenance logs}"
    AI_STATUS_TEMP="$(mktemp)"
    : >"$AI_STATUS_TEMP"
}

ai::status_add_step() {
    local name="$1"
    local status="$2"
    local command="$3"
    local log_file="$4"
    local exit_code="$5"
    local duration_ms="$6"
    local warnings="${7:-0}"

    python3 - "$name" "$status" "$command" "$log_file" "$exit_code" "$duration_ms" "$warnings" >>"$AI_STATUS_TEMP" <<'PY'
import json
import sys

print(json.dumps({
    "name": sys.argv[1],
    "status": sys.argv[2],
    "command": sys.argv[3],
    "log_file": sys.argv[4],
    "exit_code": int(sys.argv[5]),
    "duration_ms": int(sys.argv[6]),
    "warnings": int(sys.argv[7]),
}))
PY
}

ai::status_finalize() {
    local summary_status="$1"
    local next_action="$2"
    local route_file="${3:-$AI_ROUTE_FILE}"

    python3 - "$AI_STATUS_FILE" "$AI_REPO_ROOT" "$AI_STATUS_MODE" "$summary_status" "$next_action" "$AI_STATUS_TEMP" "$route_file" <<'PY'
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

status_path = Path(sys.argv[1])
repo_root = sys.argv[2]
mode = sys.argv[3]
summary_status = sys.argv[4]
next_action = sys.argv[5]
steps_path = Path(sys.argv[6])
route_path = Path(sys.argv[7])

steps = [json.loads(line) for line in steps_path.read_text(encoding="utf-8").splitlines() if line.strip()]
route = {}
if route_path.exists():
    route = json.loads(route_path.read_text(encoding="utf-8"))

def available(name: str) -> bool:
    import shutil
    return shutil.which(name) is not None

environment = {
    "dotnet_available": available("dotnet"),
    "node_available": available("node"),
    "python_available": available("python3"),
    "make_available": available("make"),
}

status_path.write_text(json.dumps({
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "repo_root": repo_root,
    "mode": mode,
    "environment": environment,
    "route": route,
    "steps": steps,
    "summary": {
        "status": summary_status,
        "next_action": next_action,
    },
}, indent=2) + "\n", encoding="utf-8")
PY

    rm -f "$AI_STATUS_TEMP"
}

ai::run_step() {
    local name="$1"
    local status_on_failure="${2:-failed}"
    shift 2

    local command_text="$*"
    local log_file="$AI_LOG_DIR/${name}.log"
    local start end duration exit_code step_status

    start="$(python3 - <<'PY'
import time
print(int(time.time() * 1000))
PY
)"

    if "$@" >"$log_file" 2>&1; then
        exit_code=0
        step_status="passed"
    else
        exit_code=$?
        step_status="$status_on_failure"
    fi

    end="$(python3 - <<'PY'
import time
print(int(time.time() * 1000))
PY
)"
    duration=$((end - start))

    ai::status_add_step "$name" "$step_status" "$command_text" "$log_file" "$exit_code" "$duration"

    if [[ "$step_status" == "failed" ]]; then
        cat "$log_file"
        return "$exit_code"
    fi

    return 0
}
