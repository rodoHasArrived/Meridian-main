"""Persistent build profiles; callers must hold the repository validation lock.

Profiles reuse incremental MSBuild output, never test reports or test results.
Source content deliberately stays outside the compatibility contract: MSBuild
remains responsible for detecting source edits on every normal build.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from pathlib import Path


SCHEMA_VERSION = 1
_MANAGED_PROPERTIES = frozenset({
    "configuration", "targetframework", "targetframeworks", "runtimeidentifier",
    "runtimeidentifiers", "enablefullwpfbuild", "meridianbuildisolationkey",
    "baseoutputpath", "baseintermediateoutputpath", "outputpath", "outdir",
    "intermediateoutputpath", "msbuildprojectextensionspath", "artifactspath",
    "useartifactsoutput", "restoreoutputpath", "publishdir", "publishurl",
    "vstestresultsdirectory", "vstestlogger",
    "importdirectorybuildprops", "directorybuildpropspath",
})
_DEFINITION_SUFFIXES = frozenset({".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".sln", ".slnf", ".slnx"})
_DEFINITION_NAMES = frozenset({"global.json", "nuget.config", "packages.lock.json", ".globalconfig", ".editorconfig"})
_IGNORED_DIRECTORIES = frozenset({".git", ".ai", "artifacts", "bin", "obj", "node_modules", ".build-system", "TestResults", "__pycache__"})


def normalize_properties(properties: list[str] | None) -> list[list[str]]:
    """Validate runner properties and return sorted, case-insensitive, last-wins pairs."""
    normalized: dict[str, str] = {}
    for raw in properties or []:
        if not raw:
            continue
        assignment = re.sub(r"^[-/]p:", "", raw, flags=re.IGNORECASE)
        name, separator, value = assignment.partition("=")
        if not separator or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.-]*", name):
            raise ValueError(f"Build properties require one Name=Value assignment: {raw!r}")
        if any(character in value for character in (";", ",", "\n", "\r")):
            raise ValueError(f"Compound property assignments are not supported by the runner: {raw!r}")
        name = name.casefold()
        if name in _MANAGED_PROPERTIES:
            raise ValueError(f"Build property {name!r} is managed by the runner; use its dedicated option.")
        normalized[name] = value
    return [[name, value] for name, value in sorted(normalized.items())]


def _build_definitions(repo_root: Path) -> dict[str, str]:
    """Conservatively fingerprint repo build definitions, excluding generated files."""
    definitions: dict[str, str] = {}
    for directory, children, files in os.walk(repo_root, followlinks=False):
        children[:] = sorted(
            name for name in children
            if name not in _IGNORED_DIRECTORIES
            and not name.startswith(("obj-", "bin-"))
            and not (Path(directory) / name).is_symlink()
        )
        for name in sorted(files):
            path = Path(directory) / name
            if name.endswith("_wpftmp.csproj"):
                continue
            if path.suffix.lower() not in _DEFINITION_SUFFIXES and name.lower() not in _DEFINITION_NAMES:
                continue
            if not path.resolve().is_relative_to(repo_root):
                raise ValueError(f"Profile build definition escapes the worktree: {path}")
            definitions[path.relative_to(repo_root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return dict(sorted(definitions.items()))


def _atomic_write(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2, sort_keys=True)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        Path(temporary).unlink(missing_ok=True)


def _read_manifest(path: Path) -> dict:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ValueError(f"Cannot reuse corrupt profile manifest {path}: {exc}") from exc
    if (not isinstance(payload, dict) or payload.get("schemaVersion") != SCHEMA_VERSION
            or not isinstance(payload.get("compatibility"), dict)
            or type(payload.get("built")) is not bool):
        raise ValueError(f"Cannot reuse invalid or unsupported profile manifest {path}.")
    return payload


def prepare_profile(repo_root: Path, args: argparse.Namespace, sdk_version: str) -> dict:
    """Create or validate a worktree-scoped profile under the caller's write lock.

    A mismatched manifest is never overwritten. Choose a new session name for a
    different SDK, framework, configuration or property set. A successful full
    build must be recorded before explicit restore/build skipping is accepted.
    """
    repo_root = repo_root.resolve()
    profile_name = getattr(args, "profile", None)
    if profile_name != "worktree" and not (
        isinstance(profile_name, str)
        and re.fullmatch(r"session:[A-Za-z0-9][A-Za-z0-9._-]{0,63}", profile_name)
    ):
        raise ValueError("--profile must be 'worktree' or 'session:<name>' (a portable name of 1-64 characters).")
    if not sdk_version or not sdk_version.strip():
        raise ValueError("Cannot establish profile compatibility without a resolved .NET SDK version.")
    project = (repo_root / getattr(args, "project", "Meridian.sln")).resolve()
    if not project.is_relative_to(repo_root) or not project.is_file():
        raise ValueError(f"Profile project must be an existing file inside the worktree: {project}")
    compatibility = {
        "sdkVersion": sdk_version.strip(),
        "worktree": str(repo_root),
        "project": project.relative_to(repo_root).as_posix(),
        "framework": getattr(args, "framework", None) or None,
        "configuration": getattr(args, "configuration", "Release"),
        "runtime": getattr(args, "runtime", None) or None,
        "fullWpfBuild": bool(getattr(args, "full_wpf_build", False)),
        "properties": normalize_properties(getattr(args, "property", [])),
        "buildDefinitions": _build_definitions(repo_root),
    }
    identity = json.dumps([str(repo_root), profile_name], separators=(",", ":"))
    isolation_key = "profile-" + hashlib.sha256(identity.encode("utf-8")).hexdigest()[:24]
    manifest_path = repo_root / ".ai" / "build-profiles" / f"{isolation_key}.json"
    output_paths = [repo_root / "artifacts" / kind / isolation_key for kind in ("bin", "obj")]
    for owned_path in [manifest_path, *output_paths]:
        if owned_path.resolve() != owned_path:
            raise ValueError(f"Profile manifest/output path must not cross a symlink: {owned_path}")
    profile = {
        "schemaVersion": SCHEMA_VERSION,
        "isolationKey": isolation_key,
        "profileName": profile_name,
        "manifestPath": str(manifest_path),
        "compatibility": compatibility,
        "built": False,
    }
    if manifest_path.exists():
        previous = _read_manifest(manifest_path)
        if any(previous.get(field) != profile[field] for field in ("isolationKey", "profileName", "manifestPath")):
            raise ValueError(f"Profile manifest identity is invalid: {manifest_path}")
        differing = sorted(field for field in compatibility.keys() | previous["compatibility"].keys()
                           if field not in compatibility or field not in previous["compatibility"]
                           or compatibility[field] != previous["compatibility"][field])
        if differing:
            raise ValueError(f"Profile {profile_name!r} is incompatible in {', '.join(differing)}; use a different session name.")
        profile["built"] = previous["built"]
    else:
        if any((repo_root / "artifacts" / root / isolation_key).exists() for root in ("bin", "obj")):
            raise ValueError("Profile output exists without a manifest; choose a different session name.")
    if getattr(args, "skip_restore", False) or getattr(args, "no_build", False):
        if not profile["built"]:
            raise ValueError("A new or unbuilt profile requires a successful restore and build before --skip-restore or --no-build.")
        if not all((repo_root / "artifacts" / root / isolation_key).is_dir() for root in ("bin", "obj")):
            raise ValueError("Profile output is missing; run a full restore and build before --skip-restore or --no-build.")
    if not manifest_path.exists():
        _atomic_write(manifest_path, profile)
    return profile


def _mark_profile_state(profile: dict, *, built: bool) -> None:
    path = Path(profile["manifestPath"])
    current = _read_manifest(path)
    if any(current.get(field) != profile.get(field)
           for field in ("isolationKey", "profileName", "manifestPath", "compatibility")):
        raise ValueError(f"Profile manifest changed during the build: {path}")
    current["built"] = built
    _atomic_write(path, current)
    profile["built"] = built


def mark_profile_build_started(profile: dict) -> None:
    """Invalidate previous success before restore/build, so failed builds fail safe."""
    _mark_profile_state(profile, built=False)


def mark_profile_built(profile: dict) -> None:
    """Record successful build completion while the caller still holds its lock."""
    _mark_profile_state(profile, built=True)
