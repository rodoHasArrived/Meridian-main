"""Versioned source/README review fingerprints, separate from generated freshness."""
from __future__ import annotations

import hashlib
import re

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
    "source_hash retains the reviewed source baseline. readme_hash fingerprints handwritten "
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
