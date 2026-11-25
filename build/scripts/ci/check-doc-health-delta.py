#!/usr/bin/env python3
"""Compare generated documentation health with the previous commit."""
import argparse
import json
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser(description="Reject severe documentation-health regressions when a baseline is available.")
parser.add_argument("--output", type=Path, default=Path("artifacts/docs/dashboard-delta.json"))
args = parser.parse_args()

current_path = Path("docs/status/doc-health-dashboard.json")
current = json.loads(current_path.read_text(encoding="utf-8")) if current_path.exists() else {}

previous = ""
try:
    previous = subprocess.run(
        ["git", "show", "HEAD~1:docs/status/doc-health-dashboard.json"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout
except subprocess.CalledProcessError:
    previous = ""

# generate-health-dashboard.py writes a top-level snake_case "health_score".
# Reading any other key makes every score 0.0 and silently disables this gate.
def extract_score(payload):
    score = payload.get("health_score")
    if isinstance(score, (int, float)) and not isinstance(score, bool):
        return float(score)
    return None

current_score = extract_score(current)
if current_score is None:
    raise SystemExit(f"{current_path} has no numeric health_score; the dashboard regression gate cannot run.")

baseline_available = bool(previous.strip())
previous_dashboard = json.loads(previous) if baseline_available else {}
previous_score = extract_score(previous_dashboard)
missing_previous = []
if previous_score is None:
    # First commit, or a baseline without a readable score: record the gap.
    baseline_available = False
    missing_previous.append("docs/status/doc-health-dashboard.json")
    previous_score = current_score

delta = current_score - previous_score
severe = baseline_available and (delta < -10.0)

payload = {
    'baseline_available': baseline_available,
    'current_score': current_score,
    'previous_score': previous_score,
    'delta': delta,
    'missing_previous_files': missing_previous,
    'severe': severe,
}

args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

if severe:
    raise SystemExit(
        f"Severe dashboard readiness regression detected ({previous_score:.1f} -> {current_score:.1f})."
    )

if not baseline_available:
    print("Baseline dashboard source was unavailable; severe regression gating was skipped")

print(json.dumps(payload, indent=2))
