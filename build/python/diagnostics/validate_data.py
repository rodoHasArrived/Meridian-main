from __future__ import annotations

import gzip
import json
from pathlib import Path


def _human_size(num_bytes: int) -> str:
    units = ["B", "KB", "MB", "GB", "TB"]
    size = float(num_bytes)
    for unit in units:
        if size < 1024 or unit == units[-1]:
            return f"{size:.1f}{unit}" if unit != "B" else f"{int(size)}{unit}"
        size /= 1024
    return f"{size:.1f}TB"


def _open_text(path: Path):
    if path.suffix == ".gz":
        return gzip.open(path, "rt", encoding="utf-8", errors="replace")
    return path.open("r", encoding="utf-8", errors="replace")


def validate_directory(data_dir: Path, sample_lines: int = 100) -> int:
    if not data_dir.exists() or not data_dir.is_dir():
        print(f"Error: Directory not found: {data_dir}")
        return 1

    files = sorted(data_dir.rglob("*.jsonl"))
    files.extend(sorted(data_dir.rglob("*.jsonl.gz")))

    print("=== Market Data Validator ===")
    print(f"Directory: {data_dir}")
    print("")
    print(f"Found {len(files)} JSONL file(s)")
    print("")

    total_lines = 0
    total_errors = 0
    valid_files = 0
    invalid_files = 0

    for path in files:
        lines = 0
        errors = 0
        with _open_text(path) as handle:
            for idx, line in enumerate(handle, start=1):
                lines += 1
                if idx <= sample_lines:
                    try:
                        json.loads(line)
                    except json.JSONDecodeError:
                        errors += 1

        total_lines += lines
        if errors == 0:
            status = "[OK]"
            valid_files += 1
        else:
            status = "[FAIL]"
            invalid_files += 1
            total_errors += errors

        size = _human_size(path.stat().st_size)
        print(f"{status} {path.name} - {lines} lines, {size}")

    print("")
    print("=== Summary ===")
    print(f"Valid files: {valid_files}")
    print(f"Invalid files: {invalid_files}")
    print(f"Total events: {total_lines}")
    print(f"Parse errors: {total_errors}")

    return 1 if invalid_files > 0 else 0
