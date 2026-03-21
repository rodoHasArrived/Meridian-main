#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

LANE="light"
INSTALL_DOTNET=0
SUMMARY=1
STATUS_FILE="${REPO_ROOT}/.ai/maintenance-status.json"
SUMMARY_FILE="${REPO_ROOT}/.ai/MAINTENANCE_STATUS.md"

DOTNET_CHANNEL="9.0"
DOTNET_INSTALL_DIR="${HOME}/.dotnet"

print_help() {
  cat <<'EOF'
Usage: bash scripts/ai/maintenance.sh [options]

Options:
  --lane <light|full>      Maintenance lane to run (default: light)
  --full                   Shortcut for --lane full
  --light                  Shortcut for --lane light
  --install-dotnet         Install .NET 9 SDK automatically when missing
  --status-file <path>     JSON status output path
  --summary-file <path>    Markdown summary output path
  --no-summary             Skip printing markdown summary to stdout
  --help                   Show this help text
EOF
}

ensure_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    return 0
  fi

  if [[ "$INSTALL_DOTNET" -ne 1 ]]; then
    echo "[maintenance] dotnet not found; rerun with --install-dotnet or use build/scripts/install/install.sh --native" >&2
    return 1
  fi

  mkdir -p "$DOTNET_INSTALL_DIR"
  local installer
  installer="$(mktemp)"

  echo "[maintenance] installing .NET SDK channel ${DOTNET_CHANNEL} into ${DOTNET_INSTALL_DIR}" >&2
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"
  rm -f "$installer"

  export PATH="${DOTNET_INSTALL_DIR}:$PATH"
  export DOTNET_ROOT="${DOTNET_INSTALL_DIR}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --lane)
      LANE="$2"
      shift 2
      ;;
    --full)
      LANE="full"
      shift
      ;;
    --light)
      LANE="light"
      shift
      ;;
    --install-dotnet)
      INSTALL_DOTNET=1
      shift
      ;;
    --status-file)
      STATUS_FILE="$2"
      shift 2
      ;;
    --summary-file)
      SUMMARY_FILE="$2"
      shift 2
      ;;
    --no-summary)
      SUMMARY=0
      shift
      ;;
    --help|-h)
      print_help
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      print_help
      exit 1
      ;;
  esac
done

if [[ "$LANE" != "light" && "$LANE" != "full" ]]; then
  echo "Invalid lane: $LANE" >&2
  exit 1
fi

ensure_dotnet || true

CMD=("python3" "build/scripts/ai-repo-updater.py" "maintenance-${LANE}" "--status-file" "$STATUS_FILE" "--summary-file" "$SUMMARY_FILE")
if [[ "$SUMMARY" -eq 1 ]]; then
  CMD+=("--summary")
fi

cd "$REPO_ROOT"
"${CMD[@]}"
