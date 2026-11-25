"""Versioned source/README review fingerprints, separate from generated freshness."""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

LEGACY_VERSION = "1.0.0"
REVIEW_VERSION = "2.0.0"
SCHEMA_ID = "meridian.source-doc-hashes"
GENERATOR_NAME = "build/scripts/docs/validate-doc-hashes.py"
GENERATOR_VERSIONS = {LEGACY_VERSION: "1.1.0", REVIEW_VERSION: "2.0.0"}
BLOCK_NAMES = ("source-roadmap-traceability", "source-todos")
LEGACY_CONTRACT = (
    "Hash drift means code or README content changed after the last source-doc hash refresh. "
    "Source paths use ordinal POSIX ordering; UTF-8 text uses LF-normalized content hashes."
)
REVIEW_CONTRACT = (
    "source_hash retains the reviewed source baseline, excluding package-lock.json files and "
    "package.json dependency version specifiers. readme_hash fingerprints handwritten "
    "README content and exact registered markers, excluding only generated block bodies. "
    "Generated block freshness is independently validated against the source/roadmap registries. "
    "Source paths use ordinal POSIX ordering; UTF-8 text uses LF-normalized content hashes."
)


def metadata(version: str) -> dict:
    if version not in GENERATOR_VERSIONS:
        raise ValueError(f"Unsupported source-doc fingerprint version: {version}")
    return {
        "schema": {"id": SCHEMA_ID, "version": version},
        "generator": {"name": GENERATOR_NAME, "version": GENERATOR_VERSIONS[version]},
        "contract": LEGACY_CONTRACT if version == LEGACY_VERSION else REVIEW_CONTRACT,
    }


def manifest_version(manifest: dict) -> str:
    if not isinstance(manifest, dict):
        raise ValueError("Source-doc manifest must be an object")
    schema = manifest.get("schema")
    if not isinstance(schema, dict) or schema.get("id") != SCHEMA_ID:
        raise ValueError("Unsupported source-doc fingerprint schema")
    version = schema.get("version")
    expected = metadata(version)
    if schema != expected["schema"] or manifest.get("generator") != expected["generator"]:
        raise ValueError("Unsupported source-doc schema or hash generator version")
    return version


def normalized_text(content: bytes) -> str:
    return content.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n")


def generated_blocks(text: str, module_id: str) -> list[tuple[int, int, str, str]]:
    """Return exact block-body spans; malformed or overlapping markers fail closed."""
    spans = []
    for name in BLOCK_NAMES:
        begin = f"<!-- {name}:begin module={module_id} -->"
        end = f"<!-- {name}:end -->"
        # Do not accept a second, misspelled, wrong-module, or non-standalone
        # marker as a way to enlarge the generated region.
        markers = list(re.finditer(r"<!--\s*" + re.escape(name) + r"\b", text))
        if (len(markers) != 2 or text.count(begin) != 1 or text.count(end) != 1
                or re.search(r"(?m)^" + re.escape(begin) + r"$", text) is None
                or re.search(r"(?m)^" + re.escape(end) + r"$", text) is None):
            raise ValueError(f"{module_id}: missing, duplicate, or malformed {name} markers")
        start = text.index(begin) + len(begin)
        stop = text.index(end)
        if stop <= start:
            raise ValueError(f"{module_id}: reversed {name} markers")
        spans.append((start, stop, begin, end))
    spans.sort()
    if spans[0][1] > spans[1][0]:
        raise ValueError(f"{module_id}: overlapping generated README blocks")
    return spans


def reviewed_readme_text(content: bytes, module_id: str) -> str:
    text = normalized_text(content)
    for start, stop, _, _ in reversed(generated_blocks(text, module_id)):
        # Keep the exact marker lines and their position among handwritten
        # sections; only renderer-owned bytes between them are excluded.
        text = text[:start] + "\n" + text[stop:]
    return text


def reviewed_readme_hash(content: bytes, module_id: str) -> str:
    return hashlib.sha256(reviewed_readme_text(content, module_id).encode("utf-8")).hexdigest()


# Reviewed fingerprints are stored one module per file so that branches which
# review different modules never touch the same file. The header holds the
# schema/generator/contract shared by every entry; it changes only on an
# explicit fingerprint migration.
HASH_DIRECTORY = "docs/source/generated/source-hashes"
HEADER_NAME = "_manifest.json"
HEADER_KEYS = frozenset({"schema", "generator", "contract"})
_MODULE_FILE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*\Z")


def module_file_name(module_id: object) -> str:
    if not isinstance(module_id, str) or _MODULE_FILE_ID.match(module_id) is None or module_id.endswith(".json"):
        raise ValueError(f"Source module ID cannot name a reviewed fingerprint file: {module_id!r}")
    return f"{module_id}.json"


def _dump(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def serialize_manifest(manifest: dict) -> dict[str, bytes]:
    """Split a logical manifest into its header file and one file per module."""
    header = {key: value for key, value in manifest.items() if key != "modules"}
    files = {HEADER_NAME: _dump(header)}
    for entry in manifest.get("modules", []):
        name = module_file_name(entry.get("id") if isinstance(entry, dict) else None)
        if name in files:
            raise ValueError(f"Duplicate reviewed module entry: {entry['id']}")
        files[name] = _dump(entry)
    return files


def assemble_manifest(files: dict[str, bytes], loads=json.loads) -> dict:
    """Rebuild the logical manifest; every file must be the header or a named module."""
    if HEADER_NAME not in files:
        raise ValueError(f"Reviewed fingerprint header {HEADER_NAME} is missing")
    header = loads(files[HEADER_NAME])
    if not isinstance(header, dict) or set(header) != HEADER_KEYS:
        raise ValueError("Reviewed fingerprint header must contain schema, generator and contract only")
    modules = []
    for name in sorted(files):
        if name == HEADER_NAME:
            continue
        entry = loads(files[name])
        if not isinstance(entry, dict) or module_file_name(entry.get("id")) != name:
            raise ValueError(f"Reviewed fingerprint file does not hold its own module entry: {name}")
        modules.append(entry)
    return {**header, "modules": sorted(modules, key=lambda entry: entry["id"])}


def read_manifest_directory(directory: Path, loads=json.loads) -> dict | None:
    """Return the logical manifest, or None when no reviewed baseline exists."""
    if directory.is_symlink():
        raise ValueError("Reviewed fingerprint directory must not be a symbolic link")
    if not directory.exists():
        return None
    if not directory.is_dir():
        raise ValueError("Reviewed fingerprint path must be a directory")
    files = {}
    for child in directory.iterdir():
        if child.is_symlink() or not child.is_file():
            raise ValueError(f"Unexpected reviewed fingerprint entry: {child.name}")
        files[child.name] = child.read_bytes()
    return assemble_manifest(files, loads)


def write_manifest_directory(directory: Path, manifest: dict) -> bool:
    """Write only the files whose bytes change and remove entries for dropped modules."""
    files = serialize_manifest(manifest)
    if directory.is_symlink():
        raise ValueError("Reviewed fingerprint directory must not be a symbolic link")
    directory.mkdir(parents=True, exist_ok=True)
    changed = False
    for child in directory.iterdir():
        if child.name in files:
            if child.is_symlink() or not child.is_file():
                raise ValueError(f"Unexpected reviewed fingerprint entry: {child.name}")
            continue
        if child.is_symlink() or not child.is_file() or not child.name.endswith(".json"):
            raise ValueError(f"Unexpected reviewed fingerprint entry: {child.name}")
        child.unlink()
        changed = True
    for name, content in files.items():
        path = directory / name
        if not path.exists() or path.read_bytes() != content:
            path.write_bytes(content)
            changed = True
    return changed
