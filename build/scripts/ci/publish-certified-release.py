#!/usr/bin/env python3
"""Resume a draft upload, verify the downloaded certified bytes, then publish once.

No asset is overwritten or deleted. A retry must supply the original validated
release directory, including its original run/attempt-bound evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile
from urllib.parse import quote
import zipfile

SHA256 = re.compile(r"[0-9a-f]{64}")
spec = importlib.util.spec_from_file_location("release_promotion", Path(__file__).with_name("verify-release-promotion.py"))
promotion = importlib.util.module_from_spec(spec)
spec.loader.exec_module(promotion)
GATES = promotion.GATES
GATE_FILE = "release-gate-evidence.json"
BUNDLE_FILE = "release-audit-evidence.zip"
MAX_ASSET_BYTES = 2 * 1024**3
MAX_RELEASE_BYTES = 4 * 1024**3
WORKFLOW = ".github/workflows/desktop-installer-packaging.yml"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_audit_bundle(path: Path, gate: dict):
    """Verify retained upstream identities and bytes without extracting inner ZIPs."""
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if (len(entries) != len({entry.filename for entry in entries})
                or any(entry.compress_type != zipfile.ZIP_STORED or entry.file_size > MAX_ASSET_BYTES
                       or stat.S_ISLNK(entry.external_attr >> 16) for entry in entries)
                or sum(entry.file_size for entry in entries) > MAX_ASSET_BYTES
                or archive.getinfo("manifest.json").file_size > 8 * 1024**2):
            raise ValueError("Invalid or oversized retained audit bundle.")
        manifest = json.loads(archive.read("manifest.json"))
        if (manifest.get("schemaVersion") != 1
                or any(str(manifest.get(key)) != str(gate.get(key))
                       for key in ("repository", "commitSha", "runId", "runAttempt"))
                or manifest.get("validations") != gate["validations"]
                or not str(manifest.get("runNumber", "")).isdigit()
                or manifest.get("workflowPath") != WORKFLOW
                or manifest.get("artifacts") != gate.get("upstreamEvidence")):
            raise ValueError("Retained upstream provenance does not match certified gate evidence.")
        required = promotion.required_evidence(str(gate["runId"]), str(manifest["runNumber"]), str(gate["runAttempt"]))
        rows = manifest["artifacts"]
        if (not isinstance(rows, list) or len(rows) != len(required)
                or any(not isinstance(row, dict) for row in rows)
                or {row.get("name") for row in rows} != required
                or set(archive.namelist()) != {"manifest.json"} | {f"artifacts/{name}.zip" for name in required}):
            raise ValueError("Retained audit bundle lacks the exact required upstream reports.")
        for row in rows:
            if (row.get("sourceCommit") != gate["commitSha"]
                    or str(row.get("workflowRunId")) != str(gate["runId"])
                    or str(row.get("workflowRunAttempt")) != str(gate["runAttempt"])
                    or type(row.get("artifactId")) is not int or row["artifactId"] <= 0):
                raise ValueError("Retained upstream artifact identity does not match certification.")
            entry = archive.getinfo(f'artifacts/{row["name"]}.zip')
            with archive.open(entry) as stream:
                actual = hashlib.file_digest(stream, "sha256").hexdigest()
            if actual != row.get("sha256") or entry.file_size != row.get("archiveBytes"):
                raise ValueError("Retained upstream archive bytes differ from certified provenance.")


def inventory(root: Path, commit: str, repository: str) -> dict[str, str]:
    """Require the promotion manifest to cover every local file, except itself."""
    gate_path = root / GATE_FILE
    if gate_path.is_symlink() or gate_path.stat().st_size > 8 * 1024**2:
        raise ValueError("Release evidence must be a regular file.")
    gate = json.loads(gate_path.read_text(encoding="utf-8"))
    if not isinstance(gate, dict) or not isinstance(gate.get("validations"), dict):
        raise ValueError("Release provenance requires a validation manifest.")
    if (gate.get("commitSha") != commit or gate.get("repository") != repository
            or gate.get("schemaVersion") != 2
            or not str(gate.get("runId", "")).isdigit()
            or not str(gate.get("runAttempt", "")).isdigit()
            or any(not isinstance(gate["validations"].get(name), dict)
                   or gate["validations"][name].get("result") != "success" for name in GATES)):
        raise ValueError("Release provenance or required validation results do not match.")
    expected = gate.get("assets")
    if not isinstance(expected, dict) or BUNDLE_FILE not in expected or GATE_FILE in expected:
        raise ValueError("Release evidence must include the durable audit bundle.")
    for name, sha in expected.items():
        if (not isinstance(name, str) or name in (".", "..")
                or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._+-]*", name)
                or not isinstance(sha, str) or not SHA256.fullmatch(sha)):
            raise ValueError("Invalid release asset inventory.")
    expected = dict(expected, **{GATE_FILE: digest(gate_path)})
    if len(expected) >= 100:
        raise ValueError("Release exceeds the 99-asset inventory bound.")
    if {path.name for path in root.iterdir()} != set(expected):
        raise ValueError("Local release inventory contains missing or unexpected assets.")
    for name, sha in expected.items():
        file = root / name
        if file.is_symlink() or not file.is_file() or file.stat().st_size > MAX_ASSET_BYTES:
            raise ValueError(f"Invalid or oversized release asset: {name}")
        if digest(file) != sha:
            raise ValueError(f"Certified release asset digest changed: {name}")
    if sum((root / name).stat().st_size for name in expected) > MAX_RELEASE_BYTES:
        raise ValueError("Certified release exceeds the 4 GiB recovery bound.")
    validate_audit_bundle(root / BUNDLE_FILE, gate)
    return expected


class GitHub:
    """Small gh CLI adapter; all subprocess arguments are data, never shell code."""
    def __init__(self, repository: str):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("Invalid repository.")
        self.repository = repository
        self.prefix = f"repos/{repository}"

    def api(self, suffix: str, *, method: str = "GET", data=None):
        command = ["gh", "api", f"{self.prefix}/{suffix}", "--method", method]
        if data is not None:
            command += ["--input", "-"]
        response = subprocess.run(command, input=None if data is None else json.dumps(data),
                                  text=True, capture_output=True, check=True)
        return json.loads(response.stdout) if response.stdout.strip() else None

    def tag_commit(self, tag: str) -> str:
        obj = self.api(f"git/ref/tags/{quote(tag, safe='')}")["object"]
        for _ in range(8):
            if obj["type"] == "commit":
                return obj["sha"]
            if obj["type"] != "tag":
                break
            obj = self.api(f"git/tags/{obj['sha']}")["object"]
        raise ValueError("Tag must resolve to a commit within eight annotated tags.")

    def release(self, tag: str):
        found = []
        for page in range(1, 11):
            rows = self.api(f"releases?per_page=100&page={page}")
            found += [row for row in rows if row.get("tag_name") == tag]
            if len(rows) < 100:
                if len(found) > 1:
                    raise ValueError("Multiple releases share the target tag.")
                return found[0] if found else None
        raise ValueError("Release pagination exceeded the explicit 1,000-release bound.")

    def create(self, tag: str, commit: str):
        return self.api("releases", method="POST", data={
            "tag_name": tag, "target_commitish": commit, "name": tag,
            "draft": True, "prerelease": "-" in tag,
            "generate_release_notes": True,
            "body": "Certified release. See release-gate-evidence.json and release-audit-evidence.zip.",
        })

    def assets(self, release_id: int):
        # Release assets are intentionally bounded; an unbounded inventory is invalid.
        rows = self.api(f"releases/{release_id}/assets?per_page=100")
        if len(rows) >= 100:
            raise ValueError("Release exceeds the 99-asset inventory bound.")
        return rows

    def upload(self, tag: str, path: Path):
        # Deliberately no --clobber: concurrent/conflicting assets fail closed.
        subprocess.run(["gh", "release", "upload", tag, str(path),
                        "--repo", self.repository], check=True)

    def download(self, asset: dict, path: Path):
        if not isinstance(asset.get("size"), int) or not 0 <= asset["size"] <= MAX_ASSET_BYTES:
            raise ValueError("Invalid or oversized remote release asset.")
        with path.open("wb") as output:
            subprocess.run(["gh", "api", f"{self.prefix}/releases/assets/{asset['id']}",
                            "-H", "Accept: application/octet-stream"], stdout=output, check=True)
        if path.stat().st_size != asset["size"]:
            raise ValueError("Downloaded release asset size differs from API metadata.")

    def verify_attestation(self, path: Path, commit: str):
        # The existing packaging action emits SPDX attestations. Source commit and
        # signer workflow are enforced in addition to the artifact digest.
        subprocess.run([
            "gh", "attestation", "verify", str(path), "--repo", self.repository,
            "--signer-workflow", f"{self.repository}/{WORKFLOW}",
            "--source-digest", commit, "--predicate-type", "https://spdx.dev/Document/v2.3",
        ], check=True)

    def publish(self, release_id: int):
        return self.api(f"releases/{release_id}", method="PATCH", data={"draft": False})

    def download_artifact(self, artifact: dict, path: Path):
        if not isinstance(artifact.get("size_in_bytes"), int) or not 0 <= artifact["size_in_bytes"] <= MAX_RELEASE_BYTES:
            raise ValueError("Invalid or oversized validated-release archive.")
        with path.open("wb") as output:
            subprocess.run(["gh", "api", f"{self.prefix}/actions/artifacts/{artifact['id']}/zip"],
                           stdout=output, check=True)
        if (path.stat().st_size != artifact["size_in_bytes"]
                or artifact.get("digest") != f"sha256:{digest(path)}"):
            raise ValueError("Validated-release archive differs from its API digest/size.")


def prepare_resume(root: Path, client, *, repository: str, commit: str, event: str,
                   ref: str, run_id: str, attempt: str) -> bool:
    """Restore the original certified bytes on a failed tag-publication job rerun."""
    if event != "push" or not re.fullmatch(r"refs/tags/v[0-9][A-Za-z0-9.+-]*", ref):
        raise ValueError("Only a production tag-push rerun can resume publication.")
    if not run_id.isdigit() or not attempt.isdigit():
        raise ValueError("Invalid publication run/attempt.")
    if int(attempt) < 2:
        return False
    candidates = []
    count = 0
    for page in range(1, 11):
        response = client.api(f"actions/runs/{run_id}/artifacts?per_page=100&page={page}")
        rows = response["artifacts"]
        count += len(rows)
        for row in rows:
            match = re.fullmatch(rf"validated-release-{re.escape(run_id)}-([1-9][0-9]*)", row.get("name", ""))
            if match and int(match[1]) < int(attempt):
                candidates.append((int(match[1]), row))
        if count == response["total_count"]:
            break
        if not rows or count > response["total_count"]:
            raise ValueError("Incomplete validated-release artifact pagination.")
    else:
        raise ValueError("Validated-release pagination exceeds 1,000 artifacts.")
    if not candidates:
        return False
    if len({number for number, _ in candidates}) != len(candidates):
        raise ValueError("Duplicate validated-release artifacts.")
    # Prefer the earliest still-retained archive. Later copies preserve its original
    # certification attempt; they never assert that certification ran again.
    available = [(number, row) for number, row in candidates if row.get("expired") is False]
    if not available:
        raise ValueError("Original certified release expired; refusing to rebuild over its draft.")
    produced_attempt, artifact = min(available, key=lambda item: item[0])
    origin = artifact.get("workflow_run", {})
    if (origin.get("head_sha") != commit or str(origin.get("id")) != run_id
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", artifact.get("digest", "") or "")):
        raise ValueError("Validated-release artifact provenance does not match.")
    if root.exists() and any(root.iterdir()):
        raise ValueError("Resume destination must be empty.")
    with tempfile.TemporaryDirectory(prefix="meridian-release-resume-") as temporary:
        archive_path = Path(temporary) / "validated.zip"
        client.download_artifact(artifact, archive_path)
        restored = Path(temporary) / "restored"
        restored.mkdir()
        with zipfile.ZipFile(archive_path) as archive:
            entries = archive.infolist()
            if (not 1 <= len(entries) < 100 or len({entry.filename for entry in entries}) != len(entries)
                    or sum(entry.file_size for entry in entries) > MAX_RELEASE_BYTES):
                raise ValueError("Invalid or oversized validated-release archive inventory.")
            for entry in entries:
                mode = entry.external_attr >> 16
                if (not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._+-]*", entry.filename)
                        or entry.is_dir() or stat.S_ISLNK(mode)
                        or (stat.S_IFMT(mode) and not stat.S_ISREG(mode))
                        or entry.file_size > MAX_ASSET_BYTES):
                    raise ValueError("Validated-release archive contains unsafe entries.")
                with archive.open(entry) as source, (restored / entry.filename).open("wb") as target:
                    shutil.copyfileobj(source, target)
        inventory(restored, commit, repository)
        gate = json.loads((restored / GATE_FILE).read_text(encoding="utf-8"))
        certified_attempt = str(gate["runAttempt"])
        if gate["runId"] != run_id or not 1 <= int(certified_attempt) <= produced_attempt:
            raise ValueError("Certified evidence belongs to another run or future attempt.")
        for original_attempt in {certified_attempt, str(produced_attempt)}:
            run = client.api(f"actions/runs/{run_id}/attempts/{original_attempt}")
            if (str(run.get("id")) != run_id or str(run.get("run_attempt")) != original_attempt
                    or run.get("head_sha") != commit or run.get("path") != WORKFLOW
                    or run.get("event") != "push"):
                raise ValueError("Original certification workflow/source identity does not match.")
        root.mkdir(parents=True, exist_ok=True)
        for source in restored.iterdir():
            shutil.copyfile(source, root / source.name)
    return True


def validate_release(release: dict, tag: str, commit: str):
    if (release.get("tag_name") != tag or release.get("target_commitish") != commit
            or type(release.get("draft")) is not bool
            or release.get("prerelease") is not ("-" in tag)
            or type(release.get("id")) is not int):
        raise ValueError("Existing release identity differs from the certified tag/commit/channel.")


def asset_rows(rows: list, expected: dict, *, complete: bool) -> dict:
    result = {}
    for row in rows:
        name = row.get("name")
        if name in expected and row.get("state") == "starter" and row.get("size") == 0:
            raise ValueError(
                f"Incomplete empty draft asset {row.get('id')} ({name}); publication is stopped. "
                "A release owner must inspect and remove that incomplete asset, then rerun using "
                "the original validated-release artifact. No certified asset was replaced.")
        if name not in expected or name in result or row.get("state") != "uploaded":
            raise ValueError("Remote release has unexpected, duplicate or incomplete assets.")
        result[name] = row
    if complete and set(result) != set(expected):
        raise ValueError("Remote release inventory is missing certified assets.")
    return result


def verify_remote(client, rows: dict, expected: dict, commit: str):
    with tempfile.TemporaryDirectory(prefix="meridian-release-verify-") as directory:
        for name, row in sorted(rows.items()):
            downloaded = Path(directory) / name
            client.download(row, downloaded)
            if digest(downloaded) != expected[name]:
                raise ValueError(f"Remote release asset conflicts with certified bytes: {name}")
            if downloaded.suffix.lower() in (".msix", ".msixbundle", ".exe"):
                client.verify_attestation(downloaded, commit)


def publish(root: Path, client, *, repository: str, commit: str, event: str, ref: str):
    if event != "push" or not re.fullmatch(r"refs/tags/v[0-9][A-Za-z0-9.+-]*", ref):
        raise ValueError("Only a production v* tag push may publish a release.")
    tag = ref.removeprefix("refs/tags/")
    expected = inventory(root, commit, repository)
    if client.tag_commit(tag) != commit:
        raise ValueError("Remote tag does not identify the certified source commit.")
    # Fail before creating a draft if the local certified packages lack provenance.
    for name in sorted(expected):
        if Path(name).suffix.lower() in (".msix", ".msixbundle", ".exe"):
            client.verify_attestation(root / name, commit)
    release = client.release(tag)
    if release is None:
        release = client.create(tag, commit)
    validate_release(release, tag, commit)
    existing = asset_rows(client.assets(release["id"]), expected, complete=not release["draft"])
    # Verify all existing bytes before resuming any interrupted upload.
    verify_remote(client, existing, expected, commit)
    if not release["draft"]:
        return "already-published"
    for name in sorted(set(expected) - set(existing)):
        client.upload(tag, root / name)
    staged = asset_rows(client.assets(release["id"]), expected, complete=True)
    verify_remote(client, staged, expected, commit)
    # Recheck metadata and inventory immediately before the only visibility mutation.
    current = client.release(tag)
    validate_release(current, tag, commit)
    if current["id"] != release["id"] or client.tag_commit(tag) != commit:
        raise ValueError("Release or tag moved during publication.")
    latest = asset_rows(client.assets(release["id"]), expected, complete=True)
    if {k: (v["id"], v["size"]) for k, v in latest.items()} != {
            k: (v["id"], v["size"]) for k, v in staged.items()}:
        raise ValueError("Release assets moved after verification.")
    if not current["draft"]:
        return "already-published"
    published = client.publish(release["id"])
    validate_release(published, tag, commit)
    if published["draft"]:
        raise ValueError("GitHub did not publish the verified draft.")
    return "published"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--prepare-resume", action="store_true")
    parser.add_argument("--readiness-output", type=Path)
    args = parser.parse_args()
    repository = os.environ["GITHUB_REPOSITORY"]
    options = dict(repository=repository, commit=os.environ["GITHUB_SHA"],
                   event=os.environ["GITHUB_EVENT_NAME"], ref=os.environ["GITHUB_REF"])
    if args.readiness_output:
        inventory(args.root, options["commit"], repository)
        gate = json.loads((args.root / GATE_FILE).read_text(encoding="utf-8"))
        if (str(gate["runId"]) != os.environ["GITHUB_RUN_ID"]
                or not 1 <= int(gate["runAttempt"]) <= int(os.environ["GITHUB_RUN_ATTEMPT"])):
            raise ValueError("Readiness cannot bind certification from another run or future attempt.")
        gate["publicationRunAttempt"] = os.environ["GITHUB_RUN_ATTEMPT"]
        gate["certifiedGateSha256"] = digest(args.root / GATE_FILE)
        args.readiness_output.parent.mkdir(parents=True, exist_ok=True)
        args.readiness_output.write_text(json.dumps(gate, indent=2) + "\n", encoding="utf-8")
    elif args.prepare_resume:
        resumed = prepare_resume(args.root, GitHub(repository), **options,
                                 run_id=os.environ["GITHUB_RUN_ID"], attempt=os.environ["GITHUB_RUN_ATTEMPT"])
        with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
            output.write(f"restored={'true' if resumed else 'false'}\n")
        print("Restored original certified bytes." if resumed else "No earlier validated release; certification remains required.")
    else:
        print(publish(args.root, GitHub(repository), **options))


if __name__ == "__main__":
    main()
