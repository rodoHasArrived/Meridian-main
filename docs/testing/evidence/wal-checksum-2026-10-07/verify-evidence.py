#!/usr/bin/env python3
"""Verify this text evidence packet using only the Python standard library."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path, PurePosixPath


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked_path(root, relative):
    candidate = PurePosixPath(relative)
    if candidate.is_absolute() or ".." in candidate.parts:
        raise ValueError(f"Unsafe evidence path: {relative}")
    path = root.joinpath(*candidate.parts)
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError(f"Evidence path escapes packet: {relative}")
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    root = args.root.resolve()
    raw_manifest = (root / "manifest.json").read_bytes()
    expected_manifest = (root / "manifest.sha256").read_text().split()[0]
    if digest(raw_manifest) != expected_manifest:
        raise ValueError("Top-level manifest digest mismatch")
    manifest = json.loads(raw_manifest)
    recorded = {entry["path"] for entry in manifest["files"]}
    actual = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
    actual -= {"manifest.json", "manifest.sha256"}
    if actual != recorded:
        raise ValueError(f"File inventory mismatch; missing={sorted(recorded-actual)}, extra={sorted(actual-recorded)}")
    compressed = 0
    for entry in manifest["files"]:
        data = checked_path(root, entry["path"]).read_bytes()
        if len(data) != entry["bytes"] or digest(data) != entry["sha256"]:
            raise ValueError(f"Copied-file digest/size mismatch: {entry['path']}")
        original = gzip.decompress(data) if entry.get("encoding") == "gzip" else data
        if entry.get("encoding") == "gzip":
            compressed += 1
        if "originalSha256" in entry:
            if len(original) != entry["originalBytes"] or digest(original) != entry["originalSha256"]:
                raise ValueError(f"Lossless original-content mismatch: {entry['path']}")
    original_hashes = 0
    for run in manifest["portableRuns"]:
        base = checked_path(root, run["path"])
        raw = (base / "run.json").read_bytes()
        if digest(raw) != run["originalManifestSha256"]:
            raise ValueError(f"Original run manifest changed: {run['label']}")
        original = json.loads(raw)
        hashes = original.get("artifactSha256", {})
        if len(hashes) != run["originalHashedFileCount"]:
            raise ValueError(f"Original run hash inventory changed: {run['label']}")
        for relative, expected in hashes.items():
            if digest(checked_path(base, relative).read_bytes()) != expected:
                raise ValueError(f"Original manifest hash mismatch: {run['label']}/{relative}")
            original_hashes += 1
        reports = list((base / "bdn" / "results").glob("*-report-full.json"))
        if len(reports) != 1:
            raise ValueError(f"Expected exactly one full BDN report: {run['label']}")
        report = json.loads(reports[0].read_bytes())
        rows = report["Benchmarks"]
        if sorted(row["Method"] for row in rows) != sorted(run["measuredStages"]):
            raise ValueError(f"Measured stage inventory changed: {run['label']}")
        if sum(len(row["Measurements"]) for row in rows) != run["rawMeasurementCount"]:
            raise ValueError(f"Raw measurement count changed: {run['label']}")
        if run.get("interruptedReceipt"):
            receipt = json.loads(checked_path(root, run["interruptedReceipt"]).read_bytes())
            for relative, expected in receipt["fileSha256"].items():
                if digest(checked_path(base, relative).read_bytes()) != expected:
                    raise ValueError(f"Interrupted-run receipt mismatch: {relative}")
                original_hashes += 1
    for proof in manifest.get("componentSourceHashProofs", []):
        receipt = json.loads(checked_path(root, proof["receiptPath"]).read_bytes())
        actual_hash = digest(checked_path(root, proof["sourcePath"]).read_bytes())
        for key in proof["receiptKeys"]:
            if actual_hash != receipt[key]:
                raise ValueError(f"Component source snapshot mismatch: {proof['sourcePath']}/{key}")
    accepted_path = manifest.get("acceptedPortableRun")
    if accepted_path:
        accepted = checked_path(root, accepted_path)
        accepted_run = json.loads((accepted / "run.json").read_bytes())
        if accepted_run["conclusion"] != "success" or accepted_run.get("worktreeDirty") is not False:
            raise ValueError("Accepted portable run must be a successful clean checkout")
        for name in ("benchmark", "validator"):
            command = accepted_run["commands"][name]
            if command["exitCode"] != 0 or command.get("timedOut"):
                raise ValueError(f"Accepted portable {name} command did not succeed")
        evidence = json.loads((accepted / "budget-evidence.json").read_bytes())
        if (evidence["measured_count"] != 8 or evidence["benchmark_result_rows"] != 8
                or evidence["violation_count"] != 0 or evidence["unmeasured_count"] != 0):
            raise ValueError("Accepted portable budget evidence is incomplete or failing")
        measured = [stage for stage in evidence["stages"] if stage["measured"]]
        if len(measured) != 8 or any(stage["status"] != "pass" for stage in measured):
            raise ValueError("Accepted portable evidence must retain all eight passing stages")
        for stage in measured:
            if (stage["actual_mean_nanos"] > stage["max_mean_nanos_per_event"]
                    or stage["actual_allocated_bytes"] > stage["max_allocated_bytes_per_event"]):
                raise ValueError(f"Accepted stage exceeds retained budget: {stage['stage_name']}")
        reports = list((accepted / "bdn" / "results").glob("*-report-full.json"))
        rows = json.loads(reports[0].read_bytes())["Benchmarks"]
        if len(rows) != 8 or any(not row["Measurements"] for row in rows):
            raise ValueError("Accepted portable report must retain raw samples for every stage")
        baseline = checked_path(root, manifest["acceptanceContractBaseline"])
        for relative in ("profile.json", "bdn/perf-budgets.json"):
            if (accepted / relative).read_bytes() != (baseline / relative).read_bytes():
                raise ValueError(f"Accepted portable contract changed: {relative}")
    print(f"PASS: {len(recorded)} copied files; {len(manifest['portableRuns'])} portable runs; "
          f"{original_hashes} original manifest/receipt hashes; {compressed} lossless gzip files")
    if accepted_path:
        print("PASS: accepted hosted run; eight passing stages with raw measurements; unchanged profile and budgets")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
