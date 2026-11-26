import hashlib
import json
import os
import platform
import subprocess
from pathlib import Path

from .utils import iso_timestamp, write_json


class BuildFingerprint:
    def __init__(self, root: Path) -> None:
        self.root = root

    def compute(self, configuration: str) -> dict:
        source_hash = self._git_tree_hash()
        if not source_hash:
            source_hash = self._hash_files()
        payload = {
            "timestamp": iso_timestamp(),
            "source_hash": source_hash,
            "tool_versions": self._tool_versions(),
            "dependency_lock_hashes": self._lock_file_hashes(),
            "environment": self._environment_snapshot(),
            "configuration": configuration,
            "os": platform.platform(),
            "architecture": platform.machine(),
        }
        fingerprint_hash = hashlib.sha256(
            json.dumps(payload, sort_keys=True).encode("utf-8")
        ).hexdigest()
        payload["fingerprint"] = fingerprint_hash
        return payload

    def write(self, path: Path, configuration: str) -> dict:
        payload = self.compute(configuration)
        write_json(path, payload)
        return payload

    def _git_tree_hash(self) -> str | None:
        try:
            result = subprocess.run(
                ["git", "rev-parse", "HEAD^{tree}"],
                cwd=str(self.root),
                capture_output=True,
                text=True,
                check=False,
            )
        except FileNotFoundError:
            return None
        if result.returncode != 0:
            return None
        return result.stdout.strip() or None

    def _hash_files(self) -> str:
        hasher = hashlib.sha256()
        files: list[Path] = []
        for current, directories, names in self.root.walk():
            # Prune before descending: VCS metadata, build outputs, and installed npm
            # packages are not source and would dominate the walk.
            directories[:] = [name for name in directories if name not in self._EXCLUDED_DIRECTORIES]
            files.extend(current / name for name in names)
        for path in sorted(files):
            if path.is_file():
                hasher.update(path.read_bytes())
        return hasher.hexdigest()

    _EXCLUDED_DIRECTORIES = frozenset({".git", "bin", "obj", "node_modules"})

    def _tool_versions(self) -> dict:
        versions = {}
        versions["dotnet"] = self._run_version(["dotnet", "--version"])
        versions["git"] = self._run_version(["git", "--version"])
        return versions

    def _run_version(self, command: list[str]) -> str:
        try:
            result = subprocess.run(command, capture_output=True, text=True, check=False)
        except FileNotFoundError:
            return "not-installed"
        return result.stdout.strip() or result.stderr.strip() or "unknown"

    def _lock_file_hashes(self) -> dict:
        lock_files = [
            "Directory.Build.props",
            "Directory.Packages.props",
            "packages.lock.json",
            "package-lock.json",
            "yarn.lock",
            "pnpm-lock.yaml",
        ]
        hashes = {}
        for name in lock_files:
            path = self.root / name
            if path.exists():
                hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        return hashes

    def _environment_snapshot(self) -> dict:
        keys = [
            "CONFIGURATION",
            "TARGET",
            "DOTNET_ENVIRONMENT",
            "DOTNET_ROOT",
            "NUGET_PACKAGES",
            "CI",
        ]
        return {key: os.getenv(key) for key in keys if os.getenv(key) is not None}
