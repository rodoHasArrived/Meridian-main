#!/usr/bin/env python3
"""Validate and publish generated PR repairs without executing pull-request code.

The publisher imports only its default-branch checkout. Candidate patches are
untrusted data; a private Git index and a recreated merge establish their scope.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import importlib.util
import io
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

WORKFLOW = ".github/workflows/generated-repair.yml"
POLICY = "build/scripts/maintained-generation-outputs.json"
MAX_RETRIES = 2
MAX_ARTIFACT = 64 * 1024 * 1024
REPAIR_MARKER = "Meridian-Generated-Repair: true"
DISPATCH_INPUTS = ("pr_number", "head_sha", "base_sha", "attempt", "root_sha")
TITLE = re.compile(
    r"generated-repair-v2 pr=([1-9][0-9]*) head=([a-f0-9]{40}) base=([a-f0-9]{40}) "
    r"attempt=([0-2]) root=([a-f0-9]{40}) workflow_sha=([a-f0-9]{40}) "
    r"ref=(refs/(?:heads|tags)/[A-Za-z0-9_./-]+)\Z"
)
# Changes to discovery, execution, dependency installation, or authorization get
# a downloadable patch. They cannot change the trusted publisher's contract.
MANUAL_PREFIXES = (".github/", "build/", "scripts/", ".codex/skills/", ".agents/skills/")
MANUAL_FILES = {
    "AGENTS.md", "CLAUDE.md", ".gitattributes", ".gitmodules", "package.json", "package-lock.json",
    "docs/source/data/source-modules.yml", "src/Meridian.Ui/dashboard/package.json",
    "src/Meridian.Ui/dashboard/package-lock.json", "src/Meridian.Ui/dashboard/vite.config.ts",
    "src/Meridian.Ui/dashboard/tsconfig.json", "src/Meridian.Ui/dashboard/tsconfig.node.json",
}


class RepairError(ValueError):
    """A closed trust boundary or a reviewable repair failure."""


def git(root: Path, *args: str, data: bytes | None = None, env: dict | None = None) -> bytes:
    settings = dict(os.environ, GIT_TERMINAL_PROMPT="0", GIT_CONFIG_NOSYSTEM="1")
    settings.update(env or {})
    result = subprocess.run(
        ["git", "--literal-pathspecs", "-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false",
         "-c", "core.attributesFile=/dev/null", "-c", "commit.gpgsign=false", *args],
        cwd=root, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=settings,
    )
    if result.returncode:
        # Do not include command lines/environment; a push can carry credentials.
        raise RepairError("Git operation failed: " + result.stderr.decode(errors="replace")[:2000])
    return result.stdout


def safe_path(path: str) -> bool:
    parts = PurePosixPath(path).parts
    return bool(path and not path.startswith("/") and "\\" not in path and ":" not in path
                and all(ord(c) >= 32 and ord(c) != 127 for c in path)
                and all(p not in {"", ".", ".."} and p.casefold() != ".git" for p in path.split("/"))
                and parts and PurePosixPath(path).as_posix() == path)


def load_policy(trusted_root: Path) -> dict:
    # Never import the candidate's Python modules or read its authorization policy.
    import yaml
    policy = json.loads((trusted_root / POLICY).read_text(encoding="utf-8"))
    if policy.get("schema_version") != 1:
        raise RepairError("Unsupported generated-output policy version")
    modules = yaml.safe_load((trusted_root / "docs/source/data/source-modules.yml").read_text(encoding="utf-8"))
    for module in modules["modules"]:
        policy["hybrid_files"][module["readme"]] = [
            [f"<!-- {name}:begin module={module['id']} -->", f"<!-- {name}:end -->"]
            for name in ("source-roadmap-traceability", "source-todos")
        ]
    paths = (policy["whole_files"] + policy["whole_trees"] + list(policy["hybrid_files"])
             + list(policy.get("protected_suffixes", {})))
    if not all(safe_path(path) for path in paths):
        raise RepairError("Unsafe path in trusted generation policy")
    return policy


def protected_content(path: str, content: bytes, policy: dict) -> bytes | None:
    if path in policy.get("protected_suffixes", {}):
        marker = policy["protected_suffixes"][path].encode()
        if content.count(marker) > 1:
            raise RepairError("Duplicate protected suffix marker")
        return content[content.index(marker):] if marker in content else b""
    if path in policy["whole_files"] or any(path.startswith(tree + "/") for tree in policy["whole_trees"]):
        return None
    if path not in policy["hybrid_files"]:
        raise RepairError("Path is outside the trusted generation policy")
    spans = []
    for start, end in policy["hybrid_files"][path]:
        first, last = start.encode(), end.encode()
        if content.count(first) != 1 or content.count(last) != 1:
            raise RepairError("Missing or duplicate generated block markers")
        left, right = content.index(first) + len(first), content.index(last)
        if right < left:
            raise RepairError("Reversed generated block markers")
        spans.append((left, right))
    immutable, cursor = [], 0
    for left, right in sorted(spans):
        if left < cursor:
            raise RepairError("Overlapping generated block markers")
        immutable.append(content[cursor:left])
        cursor = right
    return b"".join(immutable) + content[cursor:]


def tree_entries(root: Path, tree: str) -> dict[str, tuple[str, str]]:
    entries = {}
    for entry in git(root, "ls-tree", "-r", "-z", tree).split(b"\0"):
        if entry:
            metadata, name = entry.split(b"\t", 1)
            mode, kind, oid = metadata.decode().split(" ")
            path = name.decode("utf-8", errors="strict")
            entries[path] = (mode, oid)
    return entries


def validate_delta(root: Path, before_tree: str, candidate_tree: str, policy: dict) -> list[str]:
    """Authorize bytes and modes, including deletions, against the pinned baseline."""
    before, after = tree_entries(root, before_tree), tree_entries(root, candidate_tree)
    changed = sorted(path for path in before.keys() | after.keys() if before.get(path) != after.get(path))
    violations = []
    for path in changed:
        try:
            if not safe_path(path):
                raise RepairError("Unsafe candidate path")
            old, new = before.get(path), after.get(path)
            if any(entry and entry[0] != "100644" for entry in (old, new)):
                raise RepairError("Only regular nonexecutable generated files can change")
            old_bytes = git(root, "cat-file", "blob", old[1]) if old else b""
            new_bytes = git(root, "cat-file", "blob", new[1]) if new else b""
            old_protected = protected_content(path, old_bytes, policy)
            new_protected = protected_content(path, new_bytes, policy)
            if old_protected != new_protected:
                raise RepairError("Handwritten content changed")
            if old_protected is not None and (old is None or new is None):
                raise RepairError("Cannot add or delete a hybrid/protected file")
        except (RepairError, UnicodeError):
            violations.append(path)
    return violations


def apply_patch_tree(root: Path, head_sha: str, patch: bytes) -> str:
    """Apply untrusted bytes only to an isolated index; never materialize its paths."""
    if len(patch) > MAX_ARTIFACT:
        raise RepairError("Patch exceeds the repair size limit")
    with tempfile.TemporaryDirectory(prefix="generated-repair-index-") as temp:
        env = {"GIT_INDEX_FILE": str(Path(temp) / "index")}
        git(root, "read-tree", head_sha, env=env)
        if patch:
            git(root, "apply", "--cached", "--binary", "--whitespace=nowarn", "-", data=patch, env=env)
        return git(root, "write-tree", env=env).decode().strip()


def import_trusted(path: Path):
    name = "meridian_trusted_" + path.stem.replace("-", "_")
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RepairError("Cannot load trusted repair helper")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def recreate_merge(root: Path, head: str, base: str) -> str:
    # Reject links before materializing any candidate tree. Trusted helper writes
    # must never be redirected outside the disposable checkout by PR-owned links.
    for revision in (head, base):
        for path, (mode, _) in tree_entries(root, revision).items():
            if not safe_path(path) or mode not in {"100644", "100755"}:
                raise RepairError("Unsafe merge input path or file mode: " + path)
    helper = import_trusted(root / "build/scripts/repair-generated.py")
    with tempfile.TemporaryDirectory(prefix="generated-repair-merge-") as temp:
        scratch = Path(temp) / "checkout"
        git(root, "worktree", "add", "--detach", str(scratch), head)
        try:
            report = helper.prepare_merge(scratch, head, base)
            if not report["successful"]:
                raise RepairError("Trusted merge requires review: " + report["status"])
            return report["merged_tree_sha"]
        finally:
            git(root, "worktree", "remove", "--force", str(scratch))


def parse_run_metadata(title: str) -> dict:
    match = TITLE.fullmatch(title)
    if not match:
        raise RepairError("Run title lacks valid v2 repair provenance; dispatch a new run after upgrading the workflow")
    pr, head, base, attempt, original, execution, execution_ref = match.groups()
    return {"request": {"pr_number": int(pr), "head_sha": head, "base_sha": base,
                        "attempt": int(attempt), "root_sha": original},
            "workflow_execution_sha": execution, "execution_ref": execution_ref}


def parse_request(title: str) -> dict:
    # Execution evidence belongs to the run, never the dispatch inputs.
    return parse_run_metadata(title)["request"]


def verify_run_identity(run: dict, expected_id: int, expected_attempt: int, repository: str) -> dict:
    """Validate server-owned run identity before classifying an unsupported PR."""
    if (run.get("id") != expected_id or run.get("run_attempt") != expected_attempt
            or run.get("path") != WORKFLOW or run.get("status") != "completed"
            or run.get("event") not in {"pull_request_target", "workflow_dispatch"}
            or run.get("repository", {}).get("full_name") != repository):
        raise RepairError("Source run identity/event/path/attempt is not authorized")
    return parse_request(run.get("display_title", ""))


def verify_source_run(run: dict, expected_id: int, expected_attempt: int, repository: str,
                      default_branch: str) -> dict:
    request = verify_run_identity(run, expected_id, expected_attempt, repository)
    if run["event"] != "workflow_dispatch":
        raise RepairError("Target events can request trusted generation but cannot authorize candidate publication")
    metadata = parse_run_metadata(run["display_title"])
    if (run.get("head_branch") != default_branch
            or run.get("head_repository", {}).get("full_name") != repository):
        raise RepairError("Generation must be dispatched on the default branch of this repository")
    if (run.get("head_sha") != metadata["workflow_execution_sha"]
            or metadata["execution_ref"] != f"refs/heads/{default_branch}"):
        raise RepairError("Dispatch is not bound to its default-branch workflow execution SHA and ref")
    return request


def eligibility(pr: dict, repository: str, default_branch: str) -> dict | None:
    """Unsupported targets never reach candidate execution or publication."""
    if pr.get("state") != "open" or pr.get("merged", False):
        reason = "Pull request is closed or already merged"
    elif (pr.get("base", {}).get("repo", {}).get("full_name") != repository
          or pr.get("base", {}).get("ref") != default_branch):
        reason = "Automatic repair supports pull requests targeting the repository default branch only"
    elif pr.get("head", {}).get("repo") is None:
        reason = "Pull request source repository is unavailable"
    elif (pr["head"]["repo"]["full_name"] == repository and pr["head"]["ref"] == default_branch):
        reason = "Automatic repair cannot write the default branch"
    else:
        return None
    return {"status": "ineligible", "outcome": "ineligible", "reason": reason,
            "next_action": "Use the local generated-merge recovery procedure for the intended base; no remote write was attempted."}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class GitHub:
    def __init__(self, repository: str, token: str):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise RepairError("Invalid repository name")
        self.repository, self.token = repository, token
        self.prefix = f"https://api.github.com/repos/{repository}"

    def request(self, method: str, path: str, payload: dict | None = None):
        if not path.startswith("/") or path.startswith("//"):
            raise RepairError("GitHub API path must be repository-relative")
        req = urllib.request.Request(self.prefix + path, method=method,
                                     data=json.dumps(payload).encode() if payload is not None else None,
                                     headers={"Authorization": "Bearer " + self.token,
                                              "Accept": "application/vnd.github+json",
                                              "X-GitHub-Api-Version": "2022-11-28",
                                              "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=60) as response:
                data = response.read()
                return json.loads(data) if data else None
        except urllib.error.HTTPError as exc:
            raise RepairError(f"GitHub API {method} {path}: HTTP {exc.code}") from None

    def get(self, path: str):
        return self.request("GET", path)

    def download(self, artifact_id: int) -> bytes:
        # Do not forward the API bearer token to the signed storage redirect.
        request = urllib.request.Request(self.prefix + f"/actions/artifacts/{artifact_id}/zip",
                                         headers={"Authorization": "Bearer " + self.token})
        try:
            urllib.request.build_opener(NoRedirect).open(request, timeout=60)
        except urllib.error.HTTPError as exc:
            if exc.code != 302:
                raise RepairError(f"Artifact download failed: HTTP {exc.code}") from None
            location = exc.headers["Location"]
        else:
            raise RepairError("Expected a signed artifact download redirect")
        if urllib.parse.urlparse(location).scheme != "https":
            raise RepairError("Artifact download redirect must use HTTPS")
        with urllib.request.urlopen(location, timeout=60) as response:
            data = response.read(MAX_ARTIFACT + 1)
        if len(data) > MAX_ARTIFACT:
            raise RepairError("Compressed artifact exceeds size limit")
        return data


def read_artifact(data: bytes) -> dict[str, bytes]:
    allowed = {"repair.patch", "generated.patch", "report.json", "generation-report.json"}
    files = {}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        total = 0
        for info in archive.infolist():
            total += info.file_size
            mode = info.external_attr >> 16
            if (info.filename not in allowed or info.filename in files
                    or not safe_path(info.filename) or total > MAX_ARTIFACT
                    or stat.S_ISLNK(mode) or info.is_dir()):
                raise RepairError("Unexpected, duplicate, unsafe, or oversized artifact member")
            files[info.filename] = archive.read(info)
    if not {"repair.patch", "report.json"}.issubset(files):
        raise RepairError("Repair artifact is incomplete")
    return files


def request_current(pr: dict, request: dict, repository: str, default_branch: str) -> bool:
    return (pr.get("state") == "open" and not pr.get("merged", False)
            and pr["base"]["repo"]["full_name"] == repository
            and pr["base"]["ref"] == default_branch
            and pr["head"]["sha"] == request["head_sha"]
            and pr["base"]["sha"] == request["base_sha"])


def stale_result(pr: dict, request: dict, repository: str, default_branch: str) -> dict:
    if (request["attempt"] >= MAX_RETRIES or pr.get("state") != "open"
            or pr["head"]["repo"] is None or pr["head"]["repo"]["full_name"] != repository
            or pr["base"]["ref"] != default_branch):
        return {"status": "manual", "reason": "Stale request; retry budget exhausted or PR is ineligible"}
    return {"status": "retry", "reason": "Head or base changed; discard output and regenerate",
            "request": dict(request, head_sha=pr["head"]["sha"], base_sha=pr["base"]["sha"],
                            attempt=request["attempt"] + 1)}


def fetch_pr(root: Path, number: int, head: str, base: str) -> None:
    git(root, "fetch", "--no-tags", "origin", f"refs/pull/{number}/head", base)
    git(root, "cat-file", "-e", f"{head}^{{commit}}")
    git(root, "cat-file", "-e", f"{base}^{{commit}}")


def loop_suppressed(root: Path, request: dict) -> bool:
    if request["attempt"] or REPAIR_MARKER not in git(root, "show", "-s", "--format=%B", request["head_sha"]).decode():
        return False
    # A new main revision must be able to repair a branch whose last commit is
    # ours. Only the ordinary synchronize event for an already-current pair loops.
    return git(root, "merge-base", request["head_sha"], request["base_sha"]).decode().strip() == request["base_sha"]


def inspect(root: Path, api: GitHub, run_id: int, run_attempt: int, default_branch: str) -> dict:
    run = api.get(f"/actions/runs/{run_id}/attempts/{run_attempt}")
    request = verify_run_identity(run, run_id, run_attempt, api.repository)
    pr = api.get(f"/pulls/{request['pr_number']}")
    unsupported = eligibility(pr, api.repository, default_branch)
    if unsupported:
        return dict(unsupported, request=request)
    if pr["head"]["repo"] is None or pr["head"]["repo"]["full_name"] != api.repository:
        return {"status": "manual", "outcome": "ineligible", "request": request,
                "reason": "Fork PR: automatic branch writes are unavailable",
                "next_action": "Inspect the generation report; when available, use its repair.patch with docs/engineering/generated-merge-recovery.md."}
    if run["event"] == "pull_request_target":
        # REST target-run head_sha identifies the PR head, not workflow code.
        # A feature-base workflow can also forge a title before retargeting.
        # Therefore a target event may only request a new trusted default-branch
        # dispatch. Its title and artifacts can never authorize a branch write.
        request = dict(request, head_sha=pr["head"]["sha"], base_sha=pr["base"]["sha"],
                       attempt=0, root_sha=pr["head"]["sha"])
        fetch_pr(root, request["pr_number"], request["head_sha"], request["base_sha"])
        if loop_suppressed(root, request):
            return {"status": "noop", "reason": "Repair commit already retriggered authoritative CI; suppress a bot loop"}
        return {"status": "retry", "outcome": "trusted-generation-requested", "request": request,
                "reason": "Target event requests fresh generation on the trusted default branch",
                "next_action": "Dispatch attempt 0 on the default branch; only that run's independently verified candidate can proceed to publication."}
    # Only server-bound default-branch dispatches reach candidate processing.
    verify_source_run(run, run_id, run_attempt, api.repository, default_branch)
    metadata = parse_run_metadata(run["display_title"])
    execution = metadata["workflow_execution_sha"]
    git(root, "fetch", "--no-tags", "origin", execution)
    if git(root, "show", f"{execution}:{WORKFLOW}") != (root / WORKFLOW).read_bytes():
        raise RepairError("Source workflow differs from the trusted publisher revision")
    if not request_current(pr, request, api.repository, default_branch):
        return stale_result(pr, request, api.repository, default_branch)
    if pr["head"]["ref"] == default_branch:
        raise RepairError("Never write the protected/default branch")
    fetch_pr(root, request["pr_number"], request["head_sha"], request["base_sha"])
    if loop_suppressed(root, request):
        return {"status": "noop", "reason": "Repair commit already retriggered authoritative CI; suppress a bot loop"}
    merge_base = git(root, "merge-base", request["head_sha"], request["base_sha"]).decode().strip()
    changed = git(root, "diff", "--no-renames", "--name-only", "-z", merge_base, request["head_sha"]).decode().split("\0")
    manual = [name for name in changed if name in MANUAL_FILES or name.startswith(MANUAL_PREFIXES)]
    if manual:
        return {"status": "manual", "reason": "Generator, dependency, or policy changes require manual repair",
                "manual_paths": manual, "request": request}
    if run.get("conclusion") != "success":
        return {"status": "manual", "outcome": "failed", "request": request,
                "reason": "Generation failed; inspect the repair report and logs",
                "next_action": f"Read Generated Repair run {run_id}, attempt {run_attempt}; resolve its reported paths/modules before retrying."}
    artifacts = api.get(f"/actions/runs/{run_id}/artifacts?per_page=100")["artifacts"]
    name = f"generated-repair-{run_id}-{run_attempt}"
    matches = [item for item in artifacts if item["name"] == name and not item.get("expired")]
    if len(matches) != 1:
        raise RepairError("Missing or ambiguous attempt-bound repair artifact")
    artifact = matches[0]
    if artifact.get("size_in_bytes", MAX_ARTIFACT + 1) > MAX_ARTIFACT:
        raise RepairError("Repair artifact exceeds size limit")
    provenance = artifact.get("workflow_run", {})
    if provenance.get("id") != run_id or provenance.get("head_sha") != run["head_sha"]:
        raise RepairError("Artifact provenance does not match the source run")
    files = read_artifact(api.download(artifact["id"]))
    report = json.loads(files["report.json"])
    if (report.get("successful") is not True or report.get("head_sha") != request["head_sha"]
            or report.get("base_sha") != request["base_sha"]
            or report.get("patch_sha256") != hashlib.sha256(files["repair.patch"]).hexdigest()):
        raise RepairError("Repair report does not match pinned inputs and patch")
    baseline = recreate_merge(root, request["head_sha"], request["base_sha"])
    candidate = apply_patch_tree(root, request["head_sha"], files["repair.patch"])
    violations = validate_delta(root, baseline, candidate, load_policy(root))
    if violations:
        raise RepairError("Repair changes unauthorized bytes or modes: " + ", ".join(violations))
    if candidate == git(root, "rev-parse", f"{request['head_sha']}^{{tree}}").decode().strip():
        return {"status": "noop", "reason": "Pinned branch already has the generated result"}
    return {"status": "ready", "reason": "Validated generated repair and deterministic base merge",
            "request": request, "branch": pr["head"]["ref"], "tree_sha": candidate,
            "workflow_execution_sha": execution, "execution_ref": metadata["execution_ref"],
            "workflow_ref": f"{api.repository}/{WORKFLOW}@{metadata['execution_ref']}",
            "workflow_ref_derived": True}


def write_outputs(result: dict, path: Path) -> None:
    # Keep legacy status values for workflow routing; outcome is the stable
    # operational taxonomy, not a claim that repaired-revision CI has passed.
    result.setdefault("schema_version", 1)
    result.setdefault("outcome", {"manual": "review-required", "retry": "stale-retry"}.get(
        result["status"], result["status"]))
    next_actions = {
        "review-required": "Review the listed paths/modules and use docs/engineering/generated-merge-recovery.md; do not refresh all review hashes.",
        "stale-retry": "Discard stale evidence; the publisher requests a bounded generation retry for the new revisions.",
        "published": "Wait for authoritative required checks on commit_sha; publication alone is not CI success.",
        "noop": "No repair is needed; use the authoritative checks on the current revision.",
        "ready": "The trusted publisher must recheck revisions before an App-authenticated ordinary push.",
        "failed": "Inspect the run diagnostics and correct the failure; no validation bypass is permitted.",
        "refreshed": "Inspect backlog and dispatched counts; later scans drain remaining eligible requests.",
        "trusted-generation-requested": "Inspect the fresh default-branch generation run; its candidate must pass provenance, scope, and current-ref validation before any branch write.",
    }
    result.setdefault("next_action", next_actions.get(result["outcome"], "Inspect the generation report."))
    for name, variable in (("workflow_run_id", "GITHUB_RUN_ID"), ("run_attempt", "GITHUB_RUN_ATTEMPT"),
                           ("source_run_id", "SOURCE_RUN_ID"), ("source_run_attempt", "SOURCE_RUN_ATTEMPT")):
        if os.environ.get(variable, "").isdigit():
            result.setdefault(name, int(os.environ[variable]))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"status={result['status']}\n")
    summary = (f"Generated repair: {result['outcome']}. {result.get('reason', '')}\n\n"
               f"Next action: {result['next_action']}\n")
    if result.get("manual_paths"):
        summary += "\nPaths requiring review:\n" + "\n".join(f"- {path}" for path in result["manual_paths"]) + "\n"
    print(summary)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
            output.write(html.escape(summary))


def dispatch_retry(api: GitHub, request: dict, default_branch: str) -> None:
    if set(request) != set(DISPATCH_INPUTS):
        raise RepairError("Dispatch request must contain exactly the five supported inputs")
    if not 0 <= request["attempt"] <= MAX_RETRIES:
        raise RepairError("Retry attempt is out of bounds")
    api.request("POST", "/actions/workflows/generated-repair.yml/dispatches", {
        "ref": default_branch,
        "inputs": {key: str(request[key]) for key in DISPATCH_INPUTS},
    })


def publish(root: Path, api: GitHub, prepared: dict, default_branch: str) -> dict:
    if prepared["status"] not in {"ready", "retry"}:
        return prepared
    request = prepared["request"]
    pr = api.get(f"/pulls/{request['pr_number']}")
    if prepared.get("outcome") == "trusted-generation-requested":
        unsupported = eligibility(pr, api.repository, default_branch)
        if unsupported:
            return dict(unsupported, request=request)
        if pr["head"]["repo"]["full_name"] != api.repository:
            return {"status": "manual", "outcome": "ineligible", "request": request,
                    "reason": "Fork PR: automatic dispatch and branch writes are unavailable"}
        # The target event is only a wake-up. A head/base race before the first
        # trusted generation does not spend the bounded stale-output retry budget.
        request = dict(request, head_sha=pr["head"]["sha"], base_sha=pr["base"]["sha"],
                       attempt=0, root_sha=pr["head"]["sha"])
        dispatch_retry(api, request, default_branch)
        return dict(prepared, request=request, reason="Dispatched fresh generation on the trusted default branch",
                    next_action="Inspect the fresh default-branch run; only its verified candidate can proceed to publication and required CI.")
    if not request_current(pr, request, api.repository, default_branch):
        result = stale_result(pr, request, api.repository, default_branch)
        if result["status"] == "retry":
            dispatch_retry(api, result["request"], default_branch)
        return result
    if prepared["status"] == "retry":
        dispatch_retry(api, request, default_branch)
        return prepared
    if (pr["head"]["repo"] is None or pr["head"]["repo"]["full_name"] != api.repository
            or pr["head"]["ref"] != prepared["branch"] or prepared["branch"] == default_branch):
        raise RepairError("The target branch changed or is not writable by this repair")
    # Commits are constructed from an already validated tree; no checkout, hooks,
    # package manager, repository scripts, or candidate code execute with this token.
    message = ("chore: refresh generated outputs against current main\n\n" + REPAIR_MARKER
               + f"\nMeridian-Repair-Root: {request['root_sha']}\n")
    parents = ["-p", request["head_sha"]]
    ancestor = git(root, "merge-base", request["head_sha"], request["base_sha"]).decode().strip()
    if ancestor != request["base_sha"]:
        parents += ["-p", request["base_sha"]]
    commit = git(root, "commit-tree", prepared["tree_sha"], *parents, data=message.encode(), env={
                     "GIT_AUTHOR_NAME": "Meridian generated repair", "GIT_COMMITTER_NAME": "Meridian generated repair",
                     "GIT_AUTHOR_EMAIL": "generated-repair@users.noreply.github.com",
                     "GIT_COMMITTER_EMAIL": "generated-repair@users.noreply.github.com",
                 }).decode().strip()
    # Check both refs again immediately before a non-force push. Git's receive
    # protocol additionally rejects a moved head; GitHub has no atomic two-ref CAS.
    pr = api.get(f"/pulls/{request['pr_number']}")
    if not request_current(pr, request, api.repository, default_branch):
        result = stale_result(pr, request, api.repository, default_branch)
        if result["status"] == "retry":
            dispatch_retry(api, result["request"], default_branch)
        return result
    with tempfile.TemporaryDirectory(prefix="generated-repair-auth-") as temp:
        askpass = Path(temp) / "askpass.sh"
        askpass.write_text('#!/bin/sh\ncase "$1" in *Username*) echo x-access-token;; *) printf "%s\\n" "$GENERATED_REPAIR_PUSH_TOKEN";; esac\n')
        askpass.chmod(0o700)
        try:
            git(root, "push", f"https://github.com/{api.repository}.git", f"{commit}:refs/heads/{prepared['branch']}",
                env={"GIT_ASKPASS": str(askpass), "GENERATED_REPAIR_PUSH_TOKEN": api.token})
        except RepairError:
            pr = api.get(f"/pulls/{request['pr_number']}")
            if not request_current(pr, request, api.repository, default_branch):
                result = stale_result(pr, request, api.repository, default_branch)
                if result["status"] == "retry":
                    dispatch_retry(api, result["request"], default_branch)
                return result
            raise
    pr = api.get(f"/pulls/{request['pr_number']}")
    published_request = dict(request, head_sha=commit)
    if not request_current(pr, published_request, api.repository, default_branch):
        result = stale_result(pr, published_request, api.repository, default_branch)
        if result["status"] == "retry":
            dispatch_retry(api, result["request"], default_branch)
        return dict(result, commit_sha=commit)
    return {"status": "published", "reason": "App push retriggers fresh checks", "commit_sha": commit}


def prepare_generation(root: Path, api: GitHub, run_id: int, default_branch: str, output: Path) -> dict:
    run = api.get(f"/actions/runs/{run_id}")
    request = parse_request(run["display_title"])
    pr = api.get(f"/pulls/{request['pr_number']}")
    unsupported = eligibility(pr, api.repository, default_branch)
    if unsupported:
        result = dict(unsupported, request=request)
        write_outputs(result, output)
        return result
    if not request_current(pr, request, api.repository, default_branch):
        raise RepairError("Generation request is stale; publisher will dispatch a bounded retry")
    fetch_pr(root, request["pr_number"], request["head_sha"], request["base_sha"])
    if loop_suppressed(root, request):
        result = {"status": "noop", "reason": "Repair commit detected; CI validates it without another repair"}
    elif (run.get("event") == "pull_request_target"
          and pr["head"]["repo"]["full_name"] == api.repository
          and os.environ.get("TRUSTED_DISPATCH_CONFIGURED", "").lower() == "true"):
        result = {"status": "request", "outcome": "trusted-generation-requested", "request": request,
                  "reason": "Same-repository target event delegates generation to a default-branch dispatch",
                  "next_action": "The publisher will request trusted generation; this target run does not generate a publishable candidate."}
    else:
        result = {"status": "generate", "reason": "Pinned PR and base revisions fetched", "request": request}
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.write(f"head_sha={request['head_sha']}\nbase_sha={request['base_sha']}\n")
    write_outputs(result, output)
    return result


def paginated(api: GitHub, path: str, key: str | None = None) -> list:
    values = []
    separator = "&" if "?" in path else "?"
    for page in range(1, 11):
        result = api.get(f"{path}{separator}per_page=100&page={page}")
        batch = result[key] if key else result
        values.extend(batch)
        if len(batch) < 100:
            return values
    raise RepairError("Refresh scan exceeds its 1,000-item bound; narrow the backlog manually")


def refresh(api: GitHub, default_branch: str, limit: int = 10) -> dict:
    """Bound fanout on main advancement; never retry an already requested pair."""
    base = api.get("/branches/" + urllib.parse.quote(default_branch, safe=""))["commit"]["sha"]
    runs = paginated(api, f"/actions/workflows/generated-repair.yml/runs?head_sha={base}", "workflow_runs")
    seen = set()
    for run in runs:
        try:
            request = parse_request(run.get("display_title", ""))
            seen.add((request["pr_number"], request["head_sha"], request["base_sha"]))
        except RepairError:
            continue
    pulls = paginated(api, "/pulls?state=open&base=" + urllib.parse.quote(default_branch, safe=""))
    eligible, skipped = [], 0
    for pr in pulls:
        if (pr["head"]["repo"] is None or pr["head"]["repo"]["full_name"] != api.repository
                or pr["head"]["ref"] == default_branch):
            continue
        head, number = pr["head"]["sha"], pr["number"]
        if (number, head, base) in seen:
            skipped += 1
            continue
        comparison = api.get(f"/compare/{base}...{head}")
        if comparison["merge_base_commit"]["sha"] == base:
            continue
        eligible.append({"pr_number": number, "head_sha": head, "base_sha": base,
                         "attempt": 0, "root_sha": head})
    for request in eligible[:limit]:
        dispatch_retry(api, request, default_branch)
    # These are observed waiting pairs, not PR creation dates or inferred ages.
    # Repeated receipts let metrics establish a lower bound on continuous wait.
    waiting = [{key: item[key] for key in ("pr_number", "head_sha", "base_sha")}
               for item in eligible[limit:]]
    return {"status": "refreshed", "dispatched": len(eligible[:limit]), "backlog": max(0, len(eligible) - limit),
            "observed_at": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
            "waiting_requests": waiting,
            "reason": f"Dispatched {len(eligible[:limit])} stale PRs; backlog {max(0, len(eligible) - limit)}; "
                      f"{skipped} head/base pairs already requested. Daily/manual refresh drains the backlog."}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "inspect", "publish", "refresh", "artifact-only"))
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY"))
    parser.add_argument("--default-branch", default="main")
    parser.add_argument("--run-id", type=int, default=0)
    parser.add_argument("--run-attempt", type=int, default=1)
    parser.add_argument("--result", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "artifact-only":
            result = json.loads(args.result.read_text(encoding="utf-8")) if args.result.exists() else {}
            result.update(status="manual", outcome="configuration-missing",
                          reason="Automatic publication/refresh is not configured; generation evidence remains available.",
                          next_action="A repository administrator must configure the generated-repair environment and repository-scoped App described in docs/engineering/ci-cd-optimization.md.")
            write_outputs(result, args.result)
            return 0
        api = GitHub(args.repository, os.environ["GH_TOKEN"])
        if args.command == "refresh":
            write_outputs(refresh(api, args.default_branch), args.result)
        elif args.command == "prepare":
            prepare_generation(args.repo.resolve(), api, args.run_id, args.default_branch, args.result)
        elif args.command == "inspect":
            write_outputs(inspect(args.repo.resolve(), api, args.run_id, args.run_attempt, args.default_branch), args.result)
        else:
            prepared = json.loads(args.result.read_text(encoding="utf-8"))
            result = publish(args.repo.resolve(), api, prepared, args.default_branch)
            # Preserve the inspected source evidence when publication creates a
            # new outcome dictionary. These fields never become dispatch inputs.
            for name in ("request", "source_run_id", "source_run_attempt",
                         "workflow_execution_sha", "execution_ref", "workflow_ref", "workflow_ref_derived"):
                if name in prepared:
                    result.setdefault(name, prepared[name])
            write_outputs(result, args.result)
        return 0
    except (RepairError, OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        write_outputs({"status": "failed", "reason": str(exc)}, args.result)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
