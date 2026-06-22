#!/usr/bin/env python3
"""Score event accounting architecture skill eval output."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("eval_json", nargs="?", help="Path to run_evals.py --json output. Reads stdin when omitted.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def load_payload(path: str | None) -> dict:
    text = Path(path).read_text(encoding="utf-8") if path else sys.stdin.read()
    return json.loads(text)


def main() -> int:
    args = parse_args()
    payload = load_payload(args.eval_json)
    results = payload.get("results", [])
    passed = sum(1 for result in results if result.get("status") == "pass")
    total = len(results)
    regressions = payload.get("regressions", [])
    score = 100 if total == 0 else round((passed / total) * 100)
    if regressions:
        score = min(score, 80)

    output = {
        "score": score,
        "passed": passed,
        "total": total,
        "regressions": regressions,
        "status": "pass" if passed == total and not regressions else "fail",
    }

    if args.json:
        print(json.dumps(output, indent=2))
    else:
        print(f"score={score} status={output['status']} passed={passed}/{total}")
        for warning in regressions:
            print(f"regression: {warning}")

    return 0 if output["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
