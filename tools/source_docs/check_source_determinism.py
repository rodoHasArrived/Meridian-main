#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from tools.source_docs.render_source_docs import render


def _hash_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _snapshot(root: Path) -> dict[str, str]:
    files = sorted([p for p in root.rglob("*") if p.is_file()])
    return {str(p.relative_to(root)): _hash_file(p) for p in files}


def main() -> int:
    baseline_root = Path("docs/generated/source")
    with tempfile.TemporaryDirectory(prefix="source-render-") as tmp:
        temp_root = Path(tmp)
        run1 = temp_root / "run1"
        run2 = temp_root / "run2"
        render(output_root=run1)
        render(output_root=run2)
        s1 = _snapshot(run1)
        s2 = _snapshot(run2)
        if s1 != s2:
            print("ERROR: render outputs are not deterministic between runs")
            return 1

    render(output_root=baseline_root)
    current = _snapshot(baseline_root)
    manifest = json.loads((baseline_root / "render-manifest.json").read_text(encoding="utf-8"))
    for artifact in manifest.get("artifacts", []):
        output = artifact["output"].removeprefix("docs/generated/source/")
        expected = artifact["sha256"]
        actual = current.get(output)
        if actual != expected:
            print(f"ERROR: hash mismatch for {output}: expected {expected}, got {actual}")
            return 1

    print("Determinism check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
