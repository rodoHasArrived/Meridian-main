#!/usr/bin/env python3
"""Merge pinned revisions and regenerate maintained outputs in an isolated worktree.

Exports reviewable patches and evidence without changing the caller's branch or
index. Generated code executes without sandboxing; only run reviewed generators
locally, and use a credential-free job for untrusted pull requests.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def load_resolver():
    spec = importlib.util.spec_from_file_location("generated_conflict_resolver", Path(__file__).with_name("resolve-generated-merge-conflicts.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(
        ["git", "--literal-pathspecs", "-c", f"core.hooksPath={os.devnull}", "-c", "core.fsmonitor=false", *args],
        cwd=root, stderr=subprocess.PIPE,
    )


def pin(root: Path, reference: str) -> str:
    return git(root, "rev-parse", "--verify", "--end-of-options", f"{reference}^{{commit}}").decode().strip()


def prepare_merge(root: Path, head_sha: str, base_sha: str) -> dict:
    """Construct the deterministic merge using *this script's* trusted helpers.

    The caller supplies a disposable clean checkout at head_sha. No executable
    from that checkout is loaded. Trusted writers can reuse this function to
    recreate the pre-generation tree before validating an untrusted patch.
    """
    if pin(root, "HEAD") != head_sha or git(root, "status", "--porcelain", "-z"):
        raise ValueError("Merge preparation requires a clean checkout at the pinned head")
    ancestors = git(root, "merge-base", "--all", head_sha, base_sha).decode().splitlines()
    if len(ancestors) != 1:
        raise ValueError("Generated repair requires exactly one merge base")
    result = subprocess.run(
        ["git", "--literal-pathspecs", "-c", f"core.hooksPath={os.devnull}", "-c", "core.fsmonitor=false",
         "-c", "rerere.enabled=false", "-c", "user.name=Meridian generated repair",
         "-c", "user.email=generated-repair@users.noreply.github.com",
         "merge", "--no-commit", "--no-ff", base_sha], cwd=root, capture_output=True,
    )
    resolver = load_resolver()
    conflicts = resolver.unmerged_paths(root)
    if result.returncode and not conflicts:
        raise ValueError("Git merge failed: " + result.stderr.decode(errors="replace").strip())
    if conflicts:
        resolver.recover(root, base_sha, True)
    conflicts = resolver.unmerged_paths(root)
    report = {"successful": False, "status": "review_required", "merge_base_sha": ancestors[0],
              "conflicts": conflicts, "reviewed_hashes": {}, "merged_tree_sha": None}
    if conflicts:
        if conflicts == [resolver.REVIEWED_MANIFEST]:
            try:
                report["reviewed_hashes"] = resolver.load_hash_merger().merge_checkout(
                    root, ancestors[0], head_sha, base_sha,
                )
            except (ValueError, OSError, subprocess.CalledProcessError) as exc:
                report["reviewed_hashes"] = {"successful": False, "decisions": {},
                                             "review_required": {"$manifest": str(exc)}}
        return report
    reviewed = resolver.load_hash_merger().merge_checkout(root, ancestors[0], head_sha, base_sha, apply=True)
    report["reviewed_hashes"] = reviewed
    if not reviewed["successful"]:
        return report
    report.update(successful=True, status="merged", merged_tree_sha=git(root, "write-tree").decode().strip())
    return report


def install_dependencies(root: Path, output: Path) -> bool:
    commands = [
        [sys.executable, "-m", "pip", "install", "--requirement", "build/scripts/docs/requirements.txt"],
        [shutil.which("npm") or "npm", "ci", "--no-fund", "--no-audit"],
        [shutil.which("npm") or "npm", "--prefix", "src/Meridian.Ui/dashboard", "ci", "--include=optional", "--no-fund", "--no-audit"],
    ]
    with (output / "dependencies.log").open("w", encoding="utf-8") as log:
        for command in commands:
            log.write("Running: " + " ".join(command) + "\n")
            log.flush()
            if subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT).returncode:
                return False
    return True


def execute_generation(root: Path, output: Path, max_passes: int) -> int:
    with (output / "generation.log").open("w", encoding="utf-8") as log:
        return subprocess.run(
            [sys.executable, str(Path(__file__).with_name("run-maintained-generation.py")),
             "--repo", str(root), "--max-passes", str(max_passes)],
            cwd=root, stdout=log, stderr=subprocess.STDOUT,
        ).returncode


def file_modes(root: Path, revision: str) -> dict[str, str]:
    modes = {}
    for entry in git(root, "ls-tree", "-r", "-z", revision).split(b"\0"):
        if entry:
            metadata, path = entry.split(b"\t", 1)
            modes[path.decode()] = metadata.split(b" ", 1)[0].decode()
    return modes


def mode_violations(root: Path, before: str, after: str) -> list[str]:
    old, new = file_modes(root, before), file_modes(root, after)
    return sorted(path for path, mode in new.items()
                  if mode != old.get(path, "100644"))


def repair(repo: Path, head_ref: str, base_ref: str, output: Path, max_passes: int = 3,
           install: bool = False, generation_runner=execute_generation) -> dict:
    root = Path(git(repo, "rev-parse", "--show-toplevel").decode().strip())
    head_sha, base_sha = pin(root, head_ref), pin(root, base_ref)
    output = output.resolve()
    if output.is_relative_to(root):
        if not output.is_relative_to(root / "artifacts"):
            raise ValueError("Repair artifacts inside the checkout must be under ignored artifacts/")
        probe = (output / "report.json").relative_to(root).as_posix()
        ignored = subprocess.run(["git", "check-ignore", "-q", "--", probe], cwd=root)
        if ignored.returncode != 0:
            raise ValueError("Repair artifact destination must be ignored by Git")
    output.mkdir(parents=True, exist_ok=True)
    for name in ("repair.patch", "generated.patch", "report.json", "generation.log", "generation-report.json", "dependencies.log"):
        path = output / name
        if path.is_symlink():
            raise ValueError(f"Artifact path must not be a symlink: {path}")
        path.unlink(missing_ok=True)
    report = {"schema_version": 1, "successful": False, "status": "failed", "head_sha": head_sha,
              "base_sha": base_sha, "merge_base_sha": None, "conflicts": [], "reviewed_hashes": {},
              "generation": None, "changed_paths": [], "patch_sha256": None,
              "generated_patch_sha256": None, "merged_tree_sha": None, "final_tree_sha": None}
    worktree = None
    try:
        with tempfile.TemporaryDirectory(prefix="meridian-generated-repair-") as temporary:
            worktree = Path(temporary) / "checkout"
            git(root, "worktree", "add", "--detach", str(worktree), head_sha)
            try:
                report.update(prepare_merge(worktree, head_sha, base_sha))
                report["successful"] = False
                if report["status"] != "merged":
                    return report
                if install and not install_dependencies(worktree, output):
                    report["status"] = "dependency_failure"
                    return report
                code = generation_runner(worktree, output, max_passes)
                evidence = worktree / "artifacts/maintained-generation"
                if evidence.exists():
                    source = evidence / "report.json"
                    if source.is_file():
                        report["generation"] = json.loads(source.read_text(encoding="utf-8"))
                        shutil.copyfile(source, output / "generation-report.json")
                    if (evidence / "logs").is_dir():
                        shutil.copytree(evidence / "logs", output / "logs", dirs_exist_ok=True)
                if code or not report["generation"] or not report["generation"].get("successful") or not report["generation"].get("converged"):
                    report["status"] = "generation_failed"
                    return report
                # Generated README blocks participate in reviewed hashes too.
                # Check again after convergence; never bless newly combined text.
                reviewed = load_resolver().load_hash_merger().merge_checkout(
                    worktree, report["merge_base_sha"], head_sha, base_sha, apply=True,
                )
                report["reviewed_hashes"] = reviewed
                if not reviewed["successful"]:
                    report["status"] = "review_required"
                    return report
                git(worktree, "add", "-A")
                git(worktree, "diff", "--cached", "--check")
                final_tree = git(worktree, "write-tree").decode().strip()
                report["final_tree_sha"] = final_tree
                violations = mode_violations(worktree, report["merged_tree_sha"], final_tree)
                if violations:
                    report.update(status="generation_failed", protection_violations=violations,
                                  error="Generation changed file modes or introduced non-regular files")
                    return report
                for name, before, digest_key in (("repair.patch", head_sha, "patch_sha256"),
                                                 ("generated.patch", report["merged_tree_sha"], "generated_patch_sha256")):
                    patch = git(worktree, "diff", "--binary", "--full-index", "--no-ext-diff", "--no-renames", before, final_tree, "--")
                    (output / name).write_bytes(patch)
                    report[digest_key] = hashlib.sha256(patch).hexdigest()
                report["changed_paths"] = [path for path in git(worktree, "diff", "--name-only", "-z", head_sha, final_tree, "--").decode().split("\0") if path]
                report.update(successful=True, status="ready")
                return report
            finally:
                git(root, "worktree", "remove", "--force", str(worktree))
                worktree = None
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        report["status"] = "failed"
        detail = exc.stderr.decode(errors="replace") if isinstance(exc, subprocess.CalledProcessError) and exc.stderr else str(exc)
        report["error"] = detail.strip()
        return report
    finally:
        (output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--head-ref", default="HEAD")
    parser.add_argument("--base-ref", default="origin/main")
    parser.add_argument("--output-dir", type=Path, default=Path("artifacts/generated-repair"))
    parser.add_argument("--max-passes", type=int, choices=range(1, 11), default=3, metavar="1..10")
    parser.add_argument("--install-dependencies", action="store_true", help="Install Python/docs and npm dependencies in the isolated checkout")
    args = parser.parse_args()
    try:
        output = args.output_dir if args.output_dir.is_absolute() else args.repo / args.output_dir
        report = repair(args.repo.resolve(), args.head_ref, args.base_ref, output, args.max_passes, args.install_dependencies)
        print(f"Repair: {report['status']}; report: {output / 'report.json'}")
        for conflict in report["conflicts"]:
            print(f"Review conflict: {conflict}")
        if report.get("error"):
            print(report["error"], file=sys.stderr)
        return 0 if report["successful"] else 2 if report["status"] == "failed" else 1
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print(f"Repair precondition failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
