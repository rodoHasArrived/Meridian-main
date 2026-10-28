#!/usr/bin/env python3
"""Preview or resolve known generated conflicts during a merge of main.

This only seeds conflicted outputs from the incoming main commit. Regeneration,
validation, and review of the complete merged tree are still required.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
import tempfile
from pathlib import Path, PurePosixPath


REVIEWED_MANIFEST = "docs/source/generated/source-hash-manifest.json"
POLICY = Path(__file__).with_name("maintained-generation-outputs.json")


def safe_path(path: str) -> bool:
    return (isinstance(path, str) and bool(path) and "\\" not in path and ":" not in path
            and not PurePosixPath(path).is_absolute()
            and all(ord(character) >= 32 and ord(character) != 127 for character in path)
            and all(part not in {"", ".", ".."} and part.casefold() != ".git" for part in path.split("/")))


def load_conflict_policy(policy_path: Path = POLICY) -> dict:
    """Load conflict authority from the trusted tooling, never the merged checkout.

    Permission to generate a file does not imply permission to discard conflicts
    in it. Required producer metadata is checked against the maintained catalog
    by the policy contract tests; absent strategies always require review.
    """
    policy = json.loads(policy_path.read_text(encoding="utf-8"))
    recovery = policy.get("conflict_recovery", {}) if isinstance(policy, dict) else {}
    if (not isinstance(policy, dict) or policy.get("schema_version") != 1
            or not isinstance(recovery, dict) or recovery.get("default_strategy") != "manual"):
        raise ValueError("Missing or unsupported generated conflict policy")
    files, trees = recovery.get("files", {}), recovery.get("trees", {})
    if not isinstance(files, dict) or not isinstance(trees, dict):
        raise ValueError("Conflict policy files and trees must be mappings")
    protected = set(policy.get("protected_suffixes", {})) | set(policy.get("hybrid_files", {}))
    trusted_root = policy_path.resolve().parents[2]
    for is_tree, records in ((False, files), (True, trees)):
        for path, record in records.items():
            if not safe_path(path) or not isinstance(record, dict):
                raise ValueError(f"Invalid conflict policy entry: {path!r}")
            strategy = record.get("strategy")
            if strategy not in {"manual", "regenerate", "protected-suffix", "reviewed-hashes"}:
                raise ValueError(f"Unknown conflict strategy: {path}")
            if is_tree and (strategy != "regenerate" or path not in policy.get("whole_trees", [])):
                raise ValueError(f"Conflict tree is not an authorized generated tree: {path}")
            if strategy == "regenerate":
                if ((not is_tree and (path not in policy.get("whole_files", []) or path in protected))
                        or (is_tree and any(name == path or name.startswith(path + "/") for name in protected))):
                    raise ValueError(f"Whole-file recovery would replace protected content: {path}")
            elif strategy == "protected-suffix" and path not in policy.get("protected_suffixes", {}):
                raise ValueError(f"No protected suffix declared: {path}")
            elif strategy == "reviewed-hashes" and path != REVIEWED_MANIFEST:
                raise ValueError(f"Unsupported reviewed manifest: {path}")
            if strategy in {"regenerate", "protected-suffix"}:
                generator = record.get("generator", "")
                source, _, script = generator.partition("#") if isinstance(generator, str) else ("", "", "")
                if record.get("required") is not True or not safe_path(source) or not (trusted_root / source).is_file():
                    raise ValueError(f"Recovery requires an available required generator: {path}")
                if script:
                    package = json.loads((trusted_root / source).read_text(encoding="utf-8"))
                    if not script.startswith("scripts.") or script[8:] not in package.get("scripts", {}):
                        raise ValueError(f"Unknown package generator: {path}")
    return policy


def conflict_strategy(path: str, policy: dict) -> str:
    recovery = policy["conflict_recovery"]
    if path in recovery["files"]:
        return recovery["files"][path]["strategy"]
    for tree, record in recovery["trees"].items():
        if path.startswith(tree + "/"):
            return record["strategy"]
    return "manual"


def load_hash_merger():
    spec = importlib.util.spec_from_file_location(
        "reviewed_hash_merger", Path(__file__).with_name("merge-reviewed-doc-hashes.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def merge_reviewed_hashes(root: Path, head_sha: str, main_sha: str, apply: bool) -> dict:
    ancestors = git(root, "merge-base", "--all", head_sha, main_sha).decode().splitlines()
    if len(ancestors) != 1:
        raise ValueError("Reviewed hash merge requires exactly one merge base")
    return load_hash_merger().merge_checkout(root, ancestors[0], head_sha, main_sha, apply=apply)



def git(root: Path, *args: str, data: bytes | None = None) -> bytes:
    result = subprocess.run(
        ["git", "--literal-pathspecs", *args], cwd=root, input=data,
        capture_output=True, check=True,
    )
    return result.stdout


def generated(path: str, policy: dict | None = None) -> bool:
    return conflict_strategy(path, policy or load_conflict_policy()) == "regenerate"


def merge_protected_suffix(root: Path, path: str, marker: str) -> bytes:
    """Seed only the generated prefix; merge all handwritten bytes three ways.

    Refuse missing parents, malformed markers, unsafe file modes and prose
    conflicts without touching either the worktree or index.
    """
    destination = root / path
    if destination.is_symlink() or any(
        parent.is_symlink() for parent in destination.parents if parent.is_relative_to(root)
    ):
        raise ValueError("Protected suffix cannot be written through a symbolic link")
    entries = git(root, "ls-files", "--unmerged", "-z", "--", path).split(b"\0")
    modes = {}
    for entry in entries:
        if entry:
            metadata, _ = entry.split(b"\t", 1)
            mode, _, stage = metadata.split()
            modes[stage] = mode
    if modes != {b"1": b"100644", b"2": b"100644", b"3": b"100644"}:
        raise ValueError("Protected suffix requires three existing regular-file parents")
    contents = [git(root, "show", f":{stage}:{path}") for stage in (1, 2, 3)]
    boundary = marker.encode("utf-8")
    suffixes = []
    for content in contents:
        if content.count(boundary) != 1:
            raise ValueError("Protected suffix marker is missing or duplicated")
        index = content.index(boundary)
        end = index + len(boundary)
        if (index and content[index - 1:index] != b"\n") or content[end:end + 1] not in {b"\n", b"\r", b""}:
            raise ValueError("Protected suffix marker must occupy its own line")
        suffixes.append(content[index:])
    with tempfile.TemporaryDirectory(prefix="meridian-protected-merge-") as temporary:
        paths = [Path(temporary) / name for name in ("ancestor", "head", "incoming")]
        for target, content in zip(paths, suffixes):
            target.write_bytes(content)
        result = subprocess.run(
            ["git", "-c", "core.attributesFile=/dev/null", "merge-file", "--stdout",
             str(paths[1]), str(paths[0]), str(paths[2])],
            capture_output=True,
        )
        if result.returncode:
            raise ValueError("Handwritten suffix has conflicts; resolve its three-way merge manually")
    return contents[2][:contents[2].index(boundary)] + result.stdout


def unmerged_paths(root: Path) -> list[str]:
    entries = git(root, "ls-files", "--unmerged", "-z").split(b"\0")
    return sorted({entry.split(b"\t", 1)[1].decode("utf-8") for entry in entries if entry})


def recover(root: Path, main_ref: str, apply: bool) -> int:
    root = Path(git(root, "rev-parse", "--show-toplevel").decode("utf-8").strip())
    merge_path = Path(git(root, "rev-parse", "--git-path", "MERGE_HEAD").decode("utf-8").strip())
    if not merge_path.is_absolute():
        merge_path = root / merge_path
    if not merge_path.is_file():
        raise ValueError("No merge is in progress. Start from a clean branch and merge origin/main first.")
    parents = merge_path.read_text(encoding="utf-8").splitlines()
    if len(parents) != 1:
        raise ValueError("Only a merge with one incoming parent is supported.")
    main_sha = git(root, "rev-parse", "--verify", "--end-of-options", f"{main_ref}^{{commit}}").decode().strip()
    if parents[0] != main_sha:
        raise ValueError(f"MERGE_HEAD does not match {main_ref}; refusing to select the wrong merge parent.")

    policy = load_conflict_policy()
    conflicts = unmerged_paths(root)
    candidates = [path for path in conflicts if generated(path, policy)]
    manual = [path for path in conflicts if not generated(path, policy)]
    for path in list(manual):
        if conflict_strategy(path, policy) != "protected-suffix":
            continue
        try:
            merged = merge_protected_suffix(root, path, policy["protected_suffixes"][path])
        except ValueError as exc:
            print(f"REVIEW     {path}: {exc}")
            continue
        if apply:
            (root / path).write_bytes(merged)
            git(root, "add", "--", path)
        manual.remove(path)
        print(f"PROTECTED  {path}: handwritten suffix merged from all three parents")
    if manual == [REVIEWED_MANIFEST] and conflict_strategy(REVIEWED_MANIFEST, policy) == "reviewed-hashes":
        try:
            head_sha = git(root, "rev-parse", "HEAD").decode().strip()
            reviewed = merge_reviewed_hashes(root, head_sha, main_sha, apply)
            if reviewed["successful"]:
                manual = []
                print("REVIEWED   Reused parent module baselines matching the merged source/README")
            else:
                for identity, reason in reviewed["review_required"].items():
                    print(f"REVIEW     {identity}: {reason}")
        except (ValueError, OSError, subprocess.CalledProcessError) as exc:
            print(f"REVIEW     Reviewed baseline could not be verified: {exc}")
    print(f"Incoming main: {main_sha}")
    for path in candidates:
        print(f"GENERATED  {path}")
    for path in manual:
        print(f"REVIEW     {path}")
    print(f"{len(candidates)} generated conflict(s); {len(manual)} conflict(s) require review.")

    if apply and candidates:
        # Use the pinned merge parent, not ours/theirs (which reverse during a
        # rebase). NUL-delimited literal paths handle spaces and wildcard names.
        # restore cannot resolve an unmerged path absent from the source tree.
        # Explicitly remove obsolete names, including rename/rename conflicts.
        incoming_paths = set(git(root, "ls-tree", "-r", "--name-only", "-z", main_sha).split(b"\0"))
        present = [path for path in candidates if path.encode("utf-8") in incoming_paths]
        deleted = [path for path in candidates if path.encode("utf-8") not in incoming_paths]
        for paths, command in (
            (present, ("restore", f"--source={main_sha}", "--staged", "--worktree")),
            (deleted, ("rm", "--force")),
        ):
            if paths:
                pathspec = b"".join(path.encode("utf-8") + b"\0" for path in paths)
                git(root, *command, "--pathspec-from-file=-", "--pathspec-file-nul", data=pathspec)
        remaining_generated = [path for path in unmerged_paths(root) if generated(path, policy)]
        if remaining_generated:
            raise ValueError("Generated conflicts remain: " + ", ".join(remaining_generated))
        print("Selected incoming output. Resolve the remaining conflicts, then regenerate and validate before committing.")
    elif not apply:
        print("Preview only. Pass --apply to stage the listed generated resolutions.")
    return 1 if manual else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="Checkout to inspect (default: current directory).")
    parser.add_argument("--main-ref", default="origin/main", help="Main ref that must match MERGE_HEAD.")
    parser.add_argument("--apply", action="store_true", help="Resolve only listed generated conflicts; default is preview.")
    args = parser.parse_args()
    try:
        return recover(args.repo, args.main_ref, args.apply)
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        detail = exc.stderr.decode("utf-8", errors="replace").strip() if isinstance(exc, subprocess.CalledProcessError) else str(exc)
        print(f"Generated conflict recovery failed: {detail}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
