#!/usr/bin/env python3
"""Bind reusable integration evidence to one commit, workflow run, and attempt."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


RECEIPT = "integration-receipt.json"
WORKFLOW = ".github/workflows/service-backed-integrations.yml"
# One xUnit class runs serially, and this one is about a third of the suite's test time, so
# it was the floor under the whole job. It runs as its own process against its own database
# while every other test runs in a second process. The two terms are exact complements, so
# together the processes select the suite filter once; evidence() checks each result's class.
TAX_LOT_JOURNAL_CLASS = "Meridian.Tests.Storage.AtomicTaxLotJournalStoreTests"
MERIDIAN_PROCESSES = {
    "tax-lot-journal": {
        "filter": f"FullyQualifiedName~{TAX_LOT_JOURNAL_CLASS}",
        "database": "meridian_tax_lot",
    },
    "remainder": {
        "filter": f"FullyQualifiedName!~{TAX_LOT_JOURNAL_CLASS}",
        "database": "meridian",
    },
}
SUITES = {
    "meridian-integrations": {
        "project": "tests/Meridian.Tests/Meridian.Tests.csproj",
        "filter": "Category=Integration&Category!=LiveProvider",
        "processes": MERIDIAN_PROCESSES,
    },
    "direct-lending-integrations": {
        "project": "tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj",
        "filter": "Category=Integration",
    },
    "demo-integrations": {
        "project": "tests/Meridian.Demo.Tests/Meridian.Demo.Tests.csproj",
        "filter": "Category=Integration&Category!=LiveProvider",
    },
}
# One set per database a test process used. Tests create and remove their own schemas, so
# these record what each database retained once its process finished.
PRIMARY_SCHEMA_FILES = ("database-schema.sql", "database-table-inventory.csv", "migration-ledger-inventory.csv")
SCHEMA_FILES = PRIMARY_SCHEMA_FILES + tuple(
    f"{process['database']}-{name}"
    for process in MERIDIAN_PROCESSES.values() if process["database"] != "meridian"
    for name in PRIMARY_SCHEMA_FILES)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def identity_from_environment() -> dict[str, str]:
    keys = {
        "commit": "GITHUB_SHA", "repository": "GITHUB_REPOSITORY",
        "runId": "GITHUB_RUN_ID", "runAttempt": "GITHUB_RUN_ATTEMPT",
        "callerWorkflowRef": "GITHUB_WORKFLOW_REF",
    }
    identity = {key: os.environ.get(variable, "") for key, variable in keys.items()}
    if (not re.fullmatch(r"[0-9a-f]{40}", identity["commit"])
            or not re.fullmatch(r"[^/\s]+/[^/\s]+", identity["repository"])
            or not all(re.fullmatch(r"[1-9][0-9]*", identity[key]) for key in ("runId", "runAttempt"))
            or not identity["callerWorkflowRef"].startswith(identity["repository"] + "/.github/workflows/")
            or "@" not in identity["callerWorkflowRef"]):
        raise ValueError("Missing or invalid GitHub commit/run/attempt/workflow identity")
    return identity


def artifact_name(value: str) -> str:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,199}", value):
        raise ValueError("Missing or invalid integration artifact name")
    return value


def evidence(root: Path) -> dict:
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Integration evidence must be a regular directory")
    inventory = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or (not path.is_file() and not path.is_dir()):
            raise ValueError("Integration evidence contains a link or special file")
        if path.is_file() and path.relative_to(root).as_posix() != RECEIPT:
            inventory[path.relative_to(root).as_posix()] = digest(path.read_bytes())
    for name in (*SCHEMA_FILES, "skip-evidence.json"):
        if name not in inventory or not (root / name).stat().st_size:
            raise ValueError(f"Required integration evidence is missing or empty: {name}")

    spec = importlib.util.spec_from_file_location(
        "integration_test_results", Path(__file__).with_name("validate-test-results.py"))
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    # Requiring each declared process by name reports which one is missing or empty,
    # instead of leaving it to a totals mismatch.
    required = list(SUITES) + [f"{prefix}-{process}" for prefix, contract in SUITES.items()
                               for process in contract.get("processes", {})]
    observed = module.collect_evidence(root, required)
    errors = module.validation_errors(observed)
    if errors:
        raise ValueError("; ".join(errors))
    reported = json.loads((root / "skip-evidence.json").read_text(encoding="utf-8"))
    if reported.get("certifiable") is not True or reported.get("totals") != observed["totals"]:
        raise ValueError("Stored TRX validation summary disagrees with the raw results")

    suites = {}
    for prefix, contract in SUITES.items():
        suite_root = root / prefix
        coverage = sorted(suite_root.rglob("coverage.cobertura.xml"))
        if not coverage:
            raise ValueError(f"Integration suite {prefix} must retain its own coverage evidence")
        for path in coverage:
            xml = ET.parse(path).getroot()
            rate = float(xml.attrib.get("line-rate", "nan"))
            if xml.tag != "coverage" or not 0 <= rate <= 1:
                raise ValueError(f"Malformed integration coverage evidence for {prefix}")
        results = sorted(suite_root.rglob(f"{prefix}*.trx"))
        if not results or results != sorted(root.rglob(f"{prefix}*.trx")):
            raise ValueError(f"TRX evidence must belong to its declared suite directory: {prefix}")
        processes = contract.get("processes", {})
        owners = {}
        for process in processes:
            # "remainder" must not also claim "<prefix>-remainder-of-..." style names.
            owned = [path for path in results if path.name.startswith(f"{prefix}-{process}_")
                     or path.name == f"{prefix}-{process}.trx"]
            if not owned:
                raise ValueError(f"Integration suite {prefix} lacks results from its {process} process")
            owners.update({path: process for path in owned})
        if processes and (set(owners) != set(results) or len(coverage) < len(processes)):
            raise ValueError(f"Integration suite {prefix} evidence does not match its declared processes")
        identities = []
        for path in results:
            for result in ET.parse(path).getroot().iter():
                if module.local_name(result.tag) == "UnitTestResult":
                    name = result.attrib.get("testName", "")
                    if not name:
                        raise ValueError("Integration TRX result lacks a test identity")
                    if path in owners and (
                            (TAX_LOT_JOURNAL_CLASS in name) != (owners[path] == "tax-lot-journal")):
                        raise ValueError(f"Integration test ran in the wrong process: {name}")
                    identities.append([path.relative_to(root).as_posix(), name,
                                       result.attrib.get("testId", "")])
        for process in processes:
            if not any(owners.get(root / identity[0]) == process for identity in identities):
                raise ValueError(f"Integration suite {prefix} has no results from its {process} process")
        suites[prefix] = dict(contract, passed=len(identities),
                             discoverySha256=digest(json.dumps(sorted(identities)).encode()))
    return {"files": inventory, "suites": suites, "totals": observed["totals"]}


def create(root: Path, name: str, identity: dict, repository_root: Path,
           reused_from: dict | None = None) -> tuple[dict, str]:
    receipt = {
        "schemaVersion": 1, "identity": identity,
        "artifactName": artifact_name(name), "producerWorkflow": WORKFLOW,
        "producerWorkflowSha256": digest((repository_root / WORKFLOW).read_bytes()),
        "result": "success", **evidence(root),
    }
    if reused_from is not None:
        receipt["reusedFrom"] = reused_from
    data = (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode()
    (root / RECEIPT).write_bytes(data)
    return receipt, digest(data)


def verify(root: Path, expected_name: str, expected_sha256: str, expected_identity: dict,
           repository_root: Path, source_result: str, source_artifact_id: str) -> dict:
    if source_result != "success":
        raise ValueError("Integration evidence producer did not succeed")
    if not re.fullmatch(r"[1-9][0-9]*", source_artifact_id):
        raise ValueError("Missing immutable integration artifact ID")
    artifact_name(expected_name)
    if not re.fullmatch(r"[0-9a-f]{64}", expected_sha256):
        raise ValueError("Missing integration receipt digest from the successful producer")
    path = root / RECEIPT
    if path.is_symlink() or not path.is_file():
        raise ValueError("Missing regular integration receipt")
    data = path.read_bytes()
    if digest(data) != expected_sha256:
        raise ValueError("Integration receipt digest differs from the producer output")
    receipt = json.loads(data)
    if (receipt.get("schemaVersion") != 1 or receipt.get("result") != "success"
            or receipt.get("artifactName") != expected_name
            or receipt.get("identity") != expected_identity
            or receipt.get("producerWorkflow") != WORKFLOW
            or receipt.get("producerWorkflowSha256") != digest((repository_root / WORKFLOW).read_bytes())):
        raise ValueError("Integration receipt is not bound to this artifact/commit/run/attempt/workflow")
    current = evidence(root)
    if any(receipt.get(key) != value for key, value in current.items()):
        raise ValueError("Integration test roster, results, or evidence bytes changed after certification")
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("create", "reuse"))
    parser.add_argument("--results-dir", type=Path, required=True)
    parser.add_argument("--artifact-name", required=True)
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[3])
    args = parser.parse_args()
    try:
        identity = identity_from_environment()
        reused_from = None
        if args.command == "reuse":
            source_id = os.environ.get("SOURCE_ARTIFACT_ID", "")
            source_name = os.environ.get("SOURCE_ARTIFACT_NAME", "")
            source_digest = os.environ.get("SOURCE_RECEIPT_SHA256", "")
            verify(args.results_dir, source_name, source_digest, identity, args.repository_root,
                   os.environ.get("SOURCE_RESULT", ""), source_id)
            provenance_path = f"integration-provenance/{source_digest}.json"
            provenance = args.results_dir / provenance_path
            provenance.parent.mkdir(exist_ok=True)
            provenance.write_bytes((args.results_dir / RECEIPT).read_bytes())
            reused_from = {"artifactId": source_id, "artifactName": source_name,
                           "receiptSha256": source_digest, "receiptPath": provenance_path}
        _, sha256 = create(args.results_dir, args.artifact_name, identity, args.repository_root, reused_from)
        if output := os.environ.get("GITHUB_OUTPUT"):
            with Path(output).open("a", encoding="utf-8") as stream:
                stream.write(f"receipt-sha256={sha256}\nartifact-name={args.artifact_name}\n")
        print(f"Validated integration evidence for {identity['commit']} "
              f"run {identity['runId']} attempt {identity['runAttempt']}")
        return 0
    except (OSError, ValueError, ET.ParseError, TypeError) as error:
        print(f"Integration evidence rejected: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
