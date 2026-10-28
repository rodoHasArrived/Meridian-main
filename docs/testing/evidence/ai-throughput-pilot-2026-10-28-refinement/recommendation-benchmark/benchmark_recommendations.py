"""Same-root, alternating-order API microbenchmark; no task-delivery speed claims."""
from datetime import datetime, timezone
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import statistics
from time import perf_counter_ns

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--root", type=Path, required=True, help="Same full repository used for both implementations")
parser.add_argument("--baseline", type=Path, required=True, help="Retained pre-change validation_plan.py module")
parser.add_argument("--output-dir", type=Path, required=True, help="Fresh directory for raw observations, plans and summary")
parser.add_argument("--baseline-revision", help="Optional provenance revision; the retained module hash is always recorded")
args = parser.parse_args()
ROOT = args.root.resolve()
OUTPUT = args.output_dir.resolve()
BASELINE = args.baseline.resolve()
if not ROOT.is_dir() or not BASELINE.is_file():
    parser.error("--root must be a directory and --baseline must be an existing module")
OUTPUT.mkdir(parents=True, exist_ok=True)
for name in ("raw-observations.jsonl", "summary.json", "plans.json"):
    if (OUTPUT / name).exists():
        parser.error("--output-dir must not already contain benchmark results; retain earlier observations")
TREATMENT = ROOT / "build/python/cli/validation_plan.py"
QUERIES = {
    "ai-python": ["build/scripts/ai/throughput_metrics.py"],
    "shared-dotnet": ["src/Meridian.Contracts/SecurityMaster/CommitmentLot.cs"],
    "guidance-docs": ["docs/ai/tooling/throughput-pilot.md"],
}

for paths in QUERIES.values():
    for path in paths:
        if not (ROOT / path).is_file():
            parser.error(f"existing-targets benchmark requires an existing file: {path}")

def provenance_path(path):
    return path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else str(path)

def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

modules = {"baseline": load("profile_baseline", BASELINE), "treatment": load("profile_treatment", TREATMENT)}
rows = []
plans = {}
# Warm both implementations once per query; warmups are excluded and declared.
for paths in QUERIES.values():
    for module in modules.values():
        module.recommend_validation(ROOT, paths)
for round_number in range(10):
    for query, paths in QUERIES.items():
        order = ("baseline", "treatment") if round_number % 2 == 0 else ("treatment", "baseline")
        pair = {}
        for position, cohort in enumerate(order):
            started_at = datetime.now(timezone.utc).isoformat()
            started = perf_counter_ns()
            result = modules[cohort].recommend_validation(ROOT, paths)
            duration = perf_counter_ns() - started
            pair[cohort] = result
            plans.setdefault(query, {})[cohort] = result
            rows.append({"query": query, "paths": paths, "round": round_number + 1, "orderPosition": position,
                         "cohort": cohort, "startedAt": started_at, "durationNs": duration,
                         "sourceCount": len(result["sources"]), "checkCount": len(result["checks"]),
                         "warnings": result["coverageWarnings"]})
        for key in ("checks", "affectedConsumers", "requiredFinalChecks", "changedPaths", "policy"):
            assert pair["baseline"][key] == pair["treatment"][key], (query, key)
        if query == "shared-dotnet":
            assert pair["baseline"]["coverageWarnings"] == pair["treatment"]["coverageWarnings"]
        else:
            added = set(pair["treatment"]["coverageWarnings"]) - set(pair["baseline"]["coverageWarnings"])
            assert not added, (query, added)
summary = {"method": "10 paired observations per query; baseline-first on odd rounds, treatment-first on even; "
           "one excluded warmup per implementation/query; same process, root, explicit path inputs and machine. "
           "Durations cover recommend_validation API only, excluding import and CLI process startup. "
           "Baseline retained before edits; source-code provenance differs by design. No task-speed claim.",
           "root": str(ROOT), "queries": QUERIES,
           "baselineModule": provenance_path(BASELINE), "baselineRevision": args.baseline_revision,
           "targetPolicy": "Every query is asserted to name an existing file before timing.",
           "baselineSha256": hashlib.sha256(BASELINE.read_bytes()).hexdigest(),
           "treatmentModule": provenance_path(TREATMENT),
           "treatmentSha256": hashlib.sha256(TREATMENT.read_bytes()).hexdigest(),
           "semanticChecksEquivalentEveryPair": True, "observations": len(rows), "results": {}}
for query in QUERIES:
    baseline = [r["durationNs"] for r in rows if r["query"] == query and r["cohort"] == "baseline"]
    treatment = [r["durationNs"] for r in rows if r["query"] == query and r["cohort"] == "treatment"]
    summary["results"][query] = {
        "baselineMedianMs": statistics.median(baseline) / 1e6,
        "treatmentMedianMs": statistics.median(treatment) / 1e6,
        "pairedTreatmentFasterCount": sum(t < b for b, t in zip(baseline, treatment)),
        "pairedObservationCount": len(baseline),
        "baselineSourceCount": len(plans[query]["baseline"]["sources"]),
        "treatmentSourceCount": len(plans[query]["treatment"]["sources"]),
        "removedWarnings": sorted(set(plans[query]["baseline"]["coverageWarnings"]) -
                                  set(plans[query]["treatment"]["coverageWarnings"]))}
(OUTPUT / "raw-observations.jsonl").write_text("".join(json.dumps(row) + "\n" for row in rows))
(OUTPUT / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
(OUTPUT / "plans.json").write_text(json.dumps(plans, indent=2) + "\n")
print(json.dumps(summary, indent=2))
