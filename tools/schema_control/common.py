"""Deterministic primitives shared by Meridian schema-control tooling.

The schema-control pipeline emits reviewable, committed artifacts.  These helpers
therefore normalize Unicode and line endings, serialize mappings in a stable order,
and avoid rewriting files whose content has not changed.
"""

from __future__ import annotations

import hashlib
import json
import unicodedata
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any


LF = "\n"
_SEVERITIES = frozenset({"error", "warning", "info"})


@dataclass(frozen=True, slots=True)
class Finding:
    """A machine-readable schema-control validation result.

    ``rule_id`` is stable API surface suitable for policy suppressions. ``path``
    and ``subject`` are optional so the same type can represent repository-file
    findings and catalog-object findings.
    """

    rule_id: str
    severity: str
    message: str
    path: str | None = None
    subject: str | None = None

    def __post_init__(self) -> None:
        if not self.rule_id.strip():
            raise ValueError("Finding rule_id cannot be empty.")
        if self.severity not in _SEVERITIES:
            raise ValueError(
                f"Unsupported finding severity '{self.severity}'. "
                f"Expected one of {sorted(_SEVERITIES)}."
            )
        if not self.message.strip():
            raise ValueError("Finding message cannot be empty.")

    @property
    def code(self) -> str:
        """Compatibility alias for consumers that call a rule identifier a code."""

        return self.rule_id

    def to_dict(self) -> dict[str, str]:
        """Return a compact, deterministic JSON-ready representation."""

        result = {
            "rule_id": self.rule_id,
            "severity": self.severity,
            "message": self.message,
        }
        if self.path is not None:
            result["path"] = normalize_text(self.path)
        if self.subject is not None:
            result["subject"] = normalize_text(self.subject)
        return result


def normalize_text(value: str) -> str:
    """Normalize text to Unicode NFC and LF line endings."""

    return unicodedata.normalize("NFC", value).replace("\r\n", LF).replace("\r", LF)


def normalize_value(value: Any) -> Any:
    """Recursively normalize a JSON-compatible value.

    Mapping keys are normalized and ordered lexicographically. Sequence order is
    preserved because arrays often encode migration or dependency order. Sets are
    sorted by their canonical compact JSON representation.
    """

    if isinstance(value, Mapping):
        normalized_items: dict[str, Any] = {}
        for key, item in value.items():
            normalized_key = normalize_text(str(key))
            if normalized_key in normalized_items:
                raise ValueError(
                    f"Mapping contains duplicate key '{normalized_key}' after Unicode normalization."
                )
            normalized_items[normalized_key] = normalize_value(item)
        return {key: normalized_items[key] for key in sorted(normalized_items)}

    if isinstance(value, (set, frozenset)):
        normalized = [normalize_value(item) for item in value]
        return sorted(
            normalized,
            key=lambda item: json.dumps(
                item,
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            ),
        )

    if isinstance(value, Sequence) and not isinstance(value, (str, bytes, bytearray)):
        return [normalize_value(item) for item in value]

    if isinstance(value, str):
        return normalize_text(value)
    if isinstance(value, Path):
        return value.as_posix()
    return value


def canonical_json(
    value: Any,
    *,
    indent: int | None = 2,
    trailing_newline: bool = True,
) -> str:
    """Serialize ``value`` as deterministic UTF-8-oriented JSON text."""

    kwargs: dict[str, Any] = {
        "ensure_ascii": False,
        "sort_keys": True,
        "allow_nan": False,
    }
    if indent is None:
        kwargs["separators"] = (",", ":")
    else:
        kwargs["indent"] = indent

    rendered = json.dumps(normalize_value(value), **kwargs)
    rendered = normalize_text(rendered)
    if trailing_newline:
        rendered = rendered.rstrip(LF) + LF
    return rendered


def sha256_bytes(value: bytes) -> str:
    """Return the lowercase SHA-256 hex digest for raw bytes."""

    return hashlib.sha256(value).hexdigest()


def sha256_text(value: str) -> str:
    """Return the SHA-256 digest of NFC/LF-normalized UTF-8 text."""

    return sha256_bytes(normalize_text(value).encode("utf-8"))


def fingerprint(value: Any) -> str:
    """Hash the compact canonical JSON representation of ``value``."""

    return sha256_text(canonical_json(value, indent=None, trailing_newline=False))


def write_text_if_changed(path: Path, value: str) -> bool:
    """Write normalized text and return whether the file changed."""

    rendered = normalize_text(value).rstrip(LF) + LF
    current = path.read_text(encoding="utf-8") if path.exists() else None
    if current is not None and normalize_text(current) == rendered:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline=LF) as handle:
        handle.write(rendered)
    return True


def write_json_if_changed(path: Path, value: Any) -> bool:
    """Write deterministic JSON and return whether the file changed."""

    return write_text_if_changed(path, canonical_json(value))
