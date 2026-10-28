"""Safe common telemetry helper; never chooses or edits the solver's answer."""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

repo = Path(__file__).resolve().parents[5]
spec = importlib.util.spec_from_file_location("pilot_metrics", repo / "build/scripts/ai/throughput_metrics.py")
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)
parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
parser.add_argument("--log", type=Path, required=True)
parser.add_argument("--trial", required=True)
parser.add_argument("action", choices=["start", "read", "verify", "correction", "finish-failed"])
parser.add_argument("paths", nargs="*")
args = parser.parse_args()


def record(kind, **fields):
    event = {"trial": args.trial, "event": kind,
             "timestamp": datetime.now(timezone.utc).isoformat(), **fields}
    metrics.validate_event(event)
    existing = metrics.read_logs([args.log]) if args.log.exists() else []
    metrics.report([*existing, event], [args.log])
    args.log.parent.mkdir(parents=True, exist_ok=True)
    with args.log.open("a") as stream:
        stream.write(json.dumps(event, sort_keys=True) + "\n")


if args.action == "start":
    pair, cohort = args.trial.rsplit("-", 1)
    record("task_started", pair=pair, cohort=cohort)
if args.action in {"read", "start"}:
    for relative in args.paths:
        record("file_read", **metrics.source(args.root, relative))
        print((args.root / relative).read_text())
elif args.action == "correction":
    record("correction")
elif args.action == "finish-failed":
    record("task_finished", instrumentationComplete=False)
else:
    attempts = sum(event["event"] == "validation_started" for event in metrics.read_logs([args.log]))
    if attempts >= 3:
        raise SystemExit("Three attempts exhausted; record finish-failed")
    validation_id = "attempt-" + str(attempts + 1)
    record("validation_started", validationId=validation_id)
    result = subprocess.run([sys.executable, str(Path(__file__).with_name("verify-trial.py")), str(args.root)])
    record("validation_finished", validationId=validation_id, result="pass" if result.returncode == 0 else "fail")
    if result.returncode == 0:
        record("task_finished", instrumentationComplete=True)
    raise SystemExit(result.returncode)
