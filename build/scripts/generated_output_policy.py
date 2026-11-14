"""Shared delivery ownership for maintained generation (never review evidence)."""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
from pathlib import Path, PurePosixPath

POLICY = 'build/scripts/maintained-generation-outputs.json'


def delivery(policy: dict) -> dict:
    config = policy.get('delivery', {})
    if config.get('default', 'reviewed-commit') != 'reviewed-commit':
        raise ValueError('Unknown generated output delivery default')
    for key, authorized in (('ci_files', 'whole_files'), ('ci_trees', 'whole_trees')):
        for name in config.get(key, []):
            path = PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts or str(path) != name or any(char in name for char in ('\\', ':', '\x00', '\n', '\r')) or '.git' in path.parts or name not in policy[authorized]:
                raise ValueError(f'CI output must have explicit whole-output authority: {name}')
            if name in policy.get('protected_suffixes', {}) or name in policy.get('hybrid_files', {}):
                raise ValueError(f'CI output contains reviewed content: {name}')
    protected = set(config.get('reviewed_baselines', [])) | set(policy.get('protected_suffixes', {})) | set(policy.get('hybrid_files', {}))
    for name in protected:
        if name in config.get('ci_files', []) or any(name == tree or name.startswith(tree + '/') for tree in config.get('ci_trees', [])):
            raise ValueError(f'CI output contains reviewed content: {name}')
    return config


def is_ci_output(name: str, policy: dict) -> bool:
    config = delivery(policy)
    return name in config.get('ci_files', []) or any(
        name.startswith(tree + '/') for tree in config.get('ci_trees', [])
    )


def ci_output_paths(root: Path, policy: dict, *, reject_symlinks: bool = True) -> list[str]:
    config = delivery(policy)
    names = set(config.get('ci_files', []))
    for tree in config.get('ci_trees', []):
        path = root / tree
        if path.is_symlink():
            names.add(tree)
            continue
        if path.exists():
            names.update(item.relative_to(root).as_posix() for item in path.rglob('*') if item.is_file() or item.is_symlink())
    links = {parent.relative_to(root).as_posix() for name in names
             for parent in (root / name, *(root / name).parents)
             if parent.is_relative_to(root) and parent.is_symlink()}
    if links and reject_symlinks:
        raise ValueError('Symlink CI output: ' + ', '.join(sorted(links)))
    names.update(links)
    return sorted(name for name in names if (root / name).is_file() or (root / name).is_symlink())


def export_ci_outputs(root: Path, output: Path, policy: dict) -> dict:
    """Export current outputs with revision and digests; drop obsolete hashed assets."""
    target = output / 'generated'
    if target.exists():
        shutil.rmtree(target)
    target.mkdir(parents=True)
    files = {}
    for name in ci_output_paths(root, policy):
        content = (root / name).read_bytes()
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
        files[name] = hashlib.sha256(content).hexdigest()
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    manifest = {'schema_version': 1, 'revision': revision, 'files': files}
    (target / 'outputs.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest
