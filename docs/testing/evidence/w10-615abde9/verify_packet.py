#!/usr/bin/env python3
"""Verify this preparation packet's integrity; never certify operator acceptance.

Source hashes are always compared with the candidate's Git objects. The repository
is discovered from this package, or supplied with --root /path/to/repository.
The checkout HEAD may be the later documentation/evidence commit.
"""

import argparse
import gzip
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
from datetime import date
from urllib.parse import unquote, urlsplit


CANDIDATE = "615abde90001ab33bd6e58e545edc7fce635e254"
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
CASES = ["S1", "S2", "S3", "S4", "S4b", "S5", "S6", "S7", "S8", "S9", "S10"] + [
    f"M{i}" for i in range(1, 12)
]
CRITERIA = {
    "MARK-C1": "A valuation cannot rest on a mark whose age or observation date falls outside policy, and the default posture blocks rather than accepts.",
    "MARK-C2": "Freshness is governed by one policy owner rather than two independently configured controls, and consolidation preserves every non-age gate the stricter control enforces today - minimum confidence, complete coverage, and a required observation date - rather than collapsing to age alone.",
    "MARK-C3": "Valuations blocked on mark freshness render as review-required with the offending positions named, on both the browser and desktop workstations.",
    "MARK-C4": "Mark age and observation date are visible wherever positions appear, and an override is bound to the position, mark observation, valuation date, and policy version it was approved for, expiring or requiring renewed review as the charter's override strategy requires, so it cannot become a standing bypass.",
    "MARK-C5": "Enabling the new default is preceded by a preview of how many current valuations it would block.",
    "MARK-C6": "Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the policy, both workstation surfaces, and the fail-closed tests.",
    "SEAM-C1": "One close-readiness projection is the shared source both workstation lanes consume rather than each lane deriving its own, and the caller or an explicit operator selection establishes the complete close scope - fund profile, ledger book, fund account, entity, and period - rather than the projection inferring any dimension from whichever workflow it happened to select; every contribution answers for that declared scope, and a dimension that is missing, ambiguous, or mismatched blocks the projection rather than being merged into it.",
    "SEAM-C2": "Every blocker names its type, count, severity, owner, and the records causing it.",
    "SEAM-C3": "A contributing lane that is unregistered, failing, stale, or out of scope makes the projection incomplete and blocking rather than silently absent, so readiness is never reported because a contributor did not answer.",
    "SEAM-C4": "The contributing services report into that projection instead of publishing independent readiness vocabularies, and the asset-class coverage service no longer reads as close readiness.",
    "SEAM-C5": "The sequencing handshake with W8-WPF-PARITY-001 is recorded so the client-side aggregation is retired rather than duplicated into the desktop lane.",
    "SEAM-C6": "Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the shared contract, the contributing services, both workstation consumers, and the blocker-projection tests.",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def relative_path(value):
    require(isinstance(value, str) and value and "\\" not in value,
            f"Invalid relative path: {value!r}")
    path = PurePosixPath(value)
    require(not path.is_absolute() and ".." not in path.parts and ":" not in value,
            f"Path escapes its declared root: {value}")
    require(path.as_posix() == value, f"Noncanonical path: {value}")
    return path


def load(package, filename):
    with (package / filename).open(encoding="utf-8") as handle:
        return json.load(handle)


def null_fields(record, fields, label):
    for field in fields:
        require(field in record and record[field] is None, f"{label}.{field} must remain null")


def pending(record, label):
    require(record.get("state") == "pending", f"{label}: operator state must remain pending")
    null_fields(record, ["identity", "timeUtc"], label)


def verify_archive(path, item, name):
    require(item.get("compression") == "gzip" and name.endswith(".gz"),
            f"Unsupported archive encoding: {name}")
    original = relative_path(item["originalPath"]).as_posix()
    require(original + ".gz" == name, f"Archive path mismatch: {name}")
    size = item["uncompressedSizeBytes"]
    require(type(size) is int and 0 < size <= MAX_ARCHIVE_BYTES,
            f"Invalid or oversized archive payload: {name}")
    digest = item["uncompressedSha256"]
    require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest) is not None,
            f"Invalid uncompressed SHA-256: {name}")
    with gzip.open(path, "rb") as handle:
        payload = handle.read(size + 1)
    require(len(payload) == size, f"Archive payload size mismatch: {name}")
    require(hashlib.sha256(payload).hexdigest() == digest,
            f"Archive payload checksum mismatch: {name}")


def verify_files(package, manifest, root):
    listed = {}
    for item in manifest["files"]:
        name = relative_path(item["path"]).as_posix()
        require(name != "manifest.json" and name not in listed,
                f"Duplicate or self-checksummed manifest entry: {name}")
        path = (package / name).resolve()
        require(path.is_relative_to(package), f"File escapes package: {name}")
        require(path.is_file(), f"Missing packet file: {name}")
        require(re.fullmatch(r"[0-9a-f]{64}", item["sha256"]) is not None,
                f"Invalid SHA-256: {name}")
        require(hashlib.sha256(path.read_bytes()).hexdigest() == item["sha256"],
                f"Checksum mismatch: {name}")
        if "compression" in item:
            verify_archive(path, item, name)
        listed[name] = path
    require({"case-records.json", "population.json", "verify_packet.py"} <= listed.keys(),
            "Manifest must checksum case records, population and validator")
    # Verify local Markdown links that address another file inside this package.
    for name, path in listed.items():
        if path.suffix != ".md":
            continue
        for target in re.findall(r"!?\[[^\]]*\]\(([^\n)]+)\)", path.read_text(encoding="utf-8")):
            target = target.strip()
            target = target[1:target.index(">")] if target.startswith("<") else target.split()[0]
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            destination = (path.parent / unquote(parsed.path)).resolve()
            if destination.is_relative_to(package):
                linked_name = destination.relative_to(package).as_posix()
                require(destination.is_file() and (linked_name in listed or linked_name == "manifest.json"),
                        f"Missing or unchecked package link: {name} -> {target}")
    source_paths = set()
    require(manifest.get("sourceHashes"), "Manifest has no candidate source hashes")
    for item in manifest["sourceHashes"]:
        name = relative_path(item["path"]).as_posix()
        require(name not in source_paths, f"Duplicate source hash: {name}")
        source_paths.add(name)
        require(re.fullmatch(r"[0-9a-f]{64}", item["sha256"]) is not None,
                f"Invalid source SHA-256: {name}")
        content = subprocess.run(["git", "show", f"{CANDIDATE}:{name}"], cwd=root,
                                 capture_output=True, check=True, timeout=30).stdout
        require(hashlib.sha256(content).hexdigest() == item["sha256"],
                f"Candidate source checksum mismatch: {name}")
    return listed


def verify_records(records, listed):
    require(records.get("implementationOrder") == ["LOT", "MARK", "SEAM", "RECON"],
            "Implementation order must remain LOT -> MARK -> SEAM -> RECON")
    require(records.get("operatorSessionOrder") == ["SEAM", "MARK"],
            "Operator sessions must remain SEAM before MARK")
    criteria = records["criteria"]
    require(len(criteria) == 12 and {row["id"] for row in criteria} == set(CRITERIA),
            "Expected exactly 12 unique criterion IDs")
    for row in criteria:
        label = row["id"]
        require(row["criterion"] == CRITERIA[label], f"Criterion snapshot changed: {label}")
        require(row["roadmapRow"] == f"W10-{label.split('-')[0]}-001",
                f"Wrong roadmap row: {label}")
        require(row["cases"] and set(row["cases"]) <= set(CASES), f"Unknown cases: {label}")
        pending(row["operatorDecision"], label)
        require(row["reproductionEvidence"] and all(p in listed for p in row["reproductionEvidence"]),
                f"Missing reproduction evidence: {label}")
    results = records["caseClientResults"]
    expected_pairs = [(case, client) for case in CASES for client in ["browser", "desktop"]]
    require([(row["case"], row["client"]) for row in results] == expected_pairs,
            "Expected 44 unique ordered browser/desktop case pairs with SEAM before MARK")
    for row in results:
        label = f"{row['case']}/{row['client']}"
        require(row.get("candidateCommit") == CANDIDATE, f"Candidate mismatch: {label}")
        require(row.get("liveExecutionState") == "pending", f"Live state must remain pending: {label}")
        null_fields(row, ["liveSessionTimeUtc", "retainedScope"], label)
        for field in ["retainedRequestResponseIds", "materialCommandIds", "approvalIds", "repairEvidenceIds"]:
            require(row.get(field) == [], f"Pending live evidence must remain empty: {label}.{field}")
        require(row["supportingEvidence"] and all(p in listed for p in row["supportingEvidence"]),
                f"Missing supporting evidence: {label}")
        pending(row["operatorDecision"], label)
    require(records.get("actualOperatorDecisions") == [], "Actual operator decisions must remain empty")
    require(records.get("rowDecisions") == {"W10-SEAM-001": "pending", "W10-MARK-001": "pending"},
            "Roadmap row decisions must remain pending")
    require(records.get("populationDefinition") == "population.json" and
            records.get("populationProvisioningState") == "planned_not_provisioned",
            "Population link/state must remain planned")


def verify_population(population):
    require(population.get("state") == "planned_not_provisioned", "Population must remain planned")
    null_fields(population["scope"], ["retainedIdentityMap", "provisioningReceipt"], "scope")
    null_fields(population["policy"], ["installedPolicyCapture"], "policy")
    require(population["foreignScope"].get("state") == "planned_not_provisioned" and
            population["foreignScope"].get("inOwnedDenominator") is False,
            "Foreign scope must remain planned and excluded from owned denominator")
    null_fields(population["foreignScope"], ["retainedIdentityMap"], "foreignScope")
    null_fields(population["reset"], ["baselineSnapshotId", "baselineHash"], "reset")
    require(population["operator"].get("decision") == "pending", "Population decision must remain pending")
    null_fields(population["operator"], ["identity", "sessionStartedAtUtc", "sessionEndedAtUtc"], "operator")
    accounts = population["accounts"]
    require(len(accounts) == 2 and len({row["key"] for row in accounts}) == 2,
            "Expected two distinct accounts")
    schedule_by_account = {row["key"]: row["scheduleKey"] for row in accounts}
    require(len(set(schedule_by_account.values())) == 2, "Expected two distinct account-bound schedules")
    for row in accounts:
        null_fields(row, ["retainedAccountId", "retainedScheduleId", "workflowId", "workflowVersion",
                          "closePlanId", "closePlanVersion", "reportPackageId", "reportRevision"], row["key"])
    positions = population["positions"]
    require(len(positions) == 8 and len({row["key"] for row in positions}) == 8,
            "Expected eight unique planned position keys")
    require(len({(row["account"], row["security"]) for row in positions}) == 8 and
            len({row["security"] for row in positions}) == 7, "Position/security denominator changed")
    blocked_schedules = set()
    blocked = 0
    quotes = {}
    policy = population["policy"]
    require(policy["maximumAgeDays"] == 3 and policy["minimumConfidence"] == "Medium" and
            policy["requireObservedDate"] is True and policy["requireCompleteCoverage"] is True,
            "Planned default freshness policy changed")
    for row in positions:
        require(row["schedule"] == schedule_by_account.get(row["account"]),
                f"Position scope/overlap mismatch: {row['key']}")
        null_fields(row, ["retainedPositionId", "retainedSecurityId", "snapshotOwner",
                          "sourceObservationId", "inputHash"], row["key"])
        shape = (row["observedOn"], row["confidence"], row["hasPositiveQuote"])
        require(quotes.setdefault(row["security"], shape) == shape,
                "A shared security cannot have differing marks at one valuation date")
        age = ((date.fromisoformat(population["valuationDate"]) - date.fromisoformat(row["observedOn"])).days
               if row["observedOn"] else None)
        is_blocked = (not row["hasPositiveQuote"] or age is None or age < 0 or age > 3 or
                      row["confidence"] not in ["Medium", "High"])
        require(row["expectedStatus"] == ("ReviewRequired" if is_blocked else "Current"),
                f"Planned status does not follow policy: {row['key']}")
        blocked += is_blocked
        if is_blocked:
            blocked_schedules.add(row["schedule"])
    require(blocked == 5 and len(blocked_schedules) == 2, "Expected five blocks across two valuation batches")
    expected = {"accounts": 2, "schedules": 2, "distinctAccountSecurityPositions": 8,
                "distinctSecurities": 7, "valuationBatches": 2, "scheduleOverlapPositions": 0,
                "provisionedPositionCount": None}
    require(population["denominators"] == expected, "Planned denominators changed")
    for field, blocked_count, affected_count in [("plannedInitialPreview", 5, 2), ("plannedRepairPreview", 0, 0)]:
        preview = population[field]
        require(preview["assessedPositionCount"] == 8 and preview["blockedPositionCount"] == blocked_count and
                preview["affectedValuationCount"] == affected_count, f"Predicted counts changed: {field}")
        null_fields(preview, ["observedResult"], field)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, help="Repository containing the candidate Git objects")
    args = parser.parse_args()
    package = Path(__file__).resolve().parent
    try:
        if args.root:
            root = args.root.resolve()
        else:
            discovery = subprocess.run(["git", "rev-parse", "--show-toplevel"], cwd=package,
                                       capture_output=True, text=True, timeout=30)
            require(discovery.returncode == 0 and discovery.stdout.strip(),
                    "Cannot discover the candidate Git repository; supply --root pointing to "
                    f"a repository containing {CANDIDATE}")
            root = Path(discovery.stdout.strip())
        manifest = load(package, "manifest.json")
        records = load(package, "case-records.json")
        population = load(package, "population.json")
        for label, document in [("manifest", manifest), ("case records", records), ("population", population)]:
            require(document.get("candidateCommit") == CANDIDATE, f"Candidate mismatch: {label}")
        listed = verify_files(package, manifest, root)
        verify_records(records, listed)
        verify_population(population)
        tree = subprocess.run(["git", "rev-parse", f"{CANDIDATE}^{{tree}}"], cwd=root,
                              capture_output=True, check=True, text=True, timeout=30).stdout.strip()
        require(records.get("candidateTree") == tree, "Candidate tree mismatch")
    except (ValueError, KeyError, TypeError, AttributeError, IndexError, OSError, EOFError,
            subprocess.SubprocessError) as error:
        print(f"packet integrity failed: {error}; acceptance pending", file=sys.stderr)
        return 1
    print("packet integrity passed; acceptance pending")
    return 0


if __name__ == "__main__":
    sys.exit(main())
