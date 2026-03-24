import json
import os
import shutil
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path


def iso_timestamp() -> str:
    return datetime.utcnow().isoformat(timespec="milliseconds") + "Z"


def ensure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def color_enabled() -> bool:
    if os.getenv("NO_COLOR") is not None:
        return False
    return sys.stdout.isatty()


def colorize(text: str, color_code: str) -> str:
    if not color_enabled():
        return text
    return f"\033[{color_code}m{text}\033[0m"


def human_duration(duration_seconds: float) -> str:
    if duration_seconds < 1:
        return f"{duration_seconds * 1000:.0f}ms"
    if duration_seconds < 60:
        return f"{duration_seconds:.2f}s"
    minutes = int(duration_seconds // 60)
    seconds = duration_seconds % 60
    return f"{minutes}m {seconds:.1f}s"


def run_command(command, cwd: Path, log_file: Path | None, verbose: bool) -> tuple[int, str, float]:
    start = time.time()
    process = subprocess.Popen(
        command,
        cwd=str(cwd),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    output_lines = []
    while True:
        line = process.stdout.readline()
        if not line and process.poll() is not None:
            break
        if line:
            output_lines.append(line)
            if verbose:
                sys.stdout.write(line)
                sys.stdout.flush()
            if log_file:
                log_file.parent.mkdir(parents=True, exist_ok=True)
                with log_file.open("a", encoding="utf-8") as handle:
                    handle.write(line)
    exit_code = process.wait()
    duration = time.time() - start
    return exit_code, "".join(output_lines), duration


def write_json(path: Path, payload: dict) -> None:
    ensure_directory(path.parent)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def which(command: str) -> str | None:
    return shutil.which(command)
