"""Read required TRX evidence without treating a zero-exit process as proof of tests."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def collect_trx(directory: Path, prefix: str) -> dict:
    files = sorted(directory.glob(f"{prefix}*.trx"))
    if not files:
        raise ValueError(f"Missing required TRX files: {directory}/{prefix}*.trx")
    counts = dict(passed=0, failed=0, skipped=0, other=0)
    identities = []
    for file in files:
        root = ET.parse(file).getroot()
        methods = {}
        for test in root.iter():
            if test.tag.rsplit("}", 1)[-1] == "UnitTest":
                method = next((m for m in test if m.tag.rsplit("}", 1)[-1] == "TestMethod"), None)
                methods[test.get("id")] = method.get("className", "") if method is not None else ""
        discovered = 0
        for result in root.iter():
            if result.tag.rsplit("}", 1)[-1] != "UnitTestResult":
                continue
            discovered += 1
            outcome = result.get("outcome", "").lower()
            key = {"passed": "passed", "failed": "failed", "notexecuted": "skipped", "skipped": "skipped"}.get(outcome, "other")
            counts[key] += 1
            # VSTest execution/test GUIDs and absolute adapter paths vary between runs.
            identities.append(f"{methods.get(result.get('testId'), '')}|{result.get('testName', '')}")
        if not discovered:
            raise ValueError(f"Zero discovered tests in required result file: {file}")
    if not counts["passed"]:
        raise ValueError(f"Required slice produced no passing tests: {directory}")
    digest = hashlib.sha256(json.dumps(sorted(identities), ensure_ascii=True).encode()).hexdigest()
    return {"counts": counts, "testIdentityDigest": digest, "testIdentities": sorted(identities),
            "trxFiles": [str(f) for f in files]}
