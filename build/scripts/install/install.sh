#!/usr/bin/env bash
set -euo pipefail

MODE="interactive"
CHECK_ONLY=0

usage() {
  cat <<'USAGE'
Usage: install.sh [--docker|--native|--check]

Options:
  --docker   Validate Docker prerequisites.
  --native   Validate native (.NET) prerequisites.
  --check    Validate common prerequisites only.
  -h, --help Show this help text.
USAGE
}

require_cmd() {
  local cmd="$1"
  local hint="$2"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "ERROR: '$cmd' is required. ${hint}" >&2
    return 1
  fi
}

check_common() {
  require_cmd git "Install git from https://git-scm.com/downloads"
}

check_native() {
  check_common
  require_cmd dotnet "Install from https://dot.net/download"
}

check_docker() {
  check_common
  require_cmd docker "Install from https://docs.docker.com/get-docker/"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --docker) MODE="docker" ;;
    --native) MODE="native" ;;
    --check) MODE="check"; CHECK_ONLY=1 ;;
    -h|--help) usage; exit 0 ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
  shift
done

case "$MODE" in
  docker)
    check_docker
    [[ "$CHECK_ONLY" -eq 1 ]] || echo "Docker prerequisites validated. Run 'make docker' to continue."
    ;;
  native)
    check_native
    [[ "$CHECK_ONLY" -eq 1 ]] || echo "Native prerequisites validated. Run 'make setup-dev' to continue."
    ;;
  check)
    check_common
    echo "Common prerequisites validated."
    ;;
  interactive)
    check_common
    echo "Select install mode: [docker|native]"
    read -r selection || true
    if [[ "${selection:-}" == "docker" ]]; then
      check_docker
      echo "Docker prerequisites validated."
    else
      check_native
      echo "Native prerequisites validated."
    fi
    ;;
  *)
    echo "Invalid mode: $MODE" >&2
    exit 2
    ;;
esac
