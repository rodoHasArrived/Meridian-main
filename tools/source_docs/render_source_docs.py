#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import unicodedata
from pathlib import Path
from typing import Any

import yaml

DATE_RE = "%Y-%m-%d"
NEWLINE = "\n"

GENERATED_ROOT = Path("docs/generated/source")
DIAGRAM_ROOT = GENERATED_ROOT / "diagrams"
MANIFEST_PATH = GENERATED_ROOT / "render-manifest.json"


def _normalize_string(value: str) -> str:
    return unicodedata.normalize("NFC", value)


def _normalize_value(value: Any) -> Any:
    if isinstance(value, dict):
        out: dict[str, Any] = {}
        for k in sorted(value.keys(), key=lambda item: _normalize_string(str(item))):
            if not isinstance(k, str):
                raise TypeError(f"Unsupported non-string key: {k!r}")
            nk = _normalize_string(k)
            out[nk] = _normalize_value(value[k])
        return out
    if isinstance(value, list):
        return [_normalize_value(i) for i in value]
    if isinstance(value, str):
        text = _normalize_string(value)
        try:
            parsed = dt.date.fromisoformat(text)
            return parsed.strftime(DATE_RE)
        except ValueError:
            return text
    if value is None or isinstance(value, (bool, int, float)):
        return value
    raise TypeError(f"Unsupported scalar type: {type(value)!r}")


def _dump_json(path: Path, value: Any) -> str:
    content = json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + NEWLINE
    path.write_text(content, encoding="utf-8", newline=NEWLINE)
    return content


def _sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _load_yaml(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


def _diagram_from_normalized(name: str, data: Any) -> str:
    node_color = "#0A66C2"
    edge_color = "#5B7083"
    lines = [
        "graph TD",
        f"classDef node fill:{node_color},stroke:{edge_color},stroke-width:1px,color:#ffffff;",
    ]

    items: list[tuple[str, str]] = []

    def walk(prefix: str, obj: Any) -> None:
        if isinstance(obj, dict):
            for key, val in obj.items():
                node_id = hashlib.sha1(f"{name}:{prefix}:{key}".encode("utf-8")).hexdigest()[:10]
                label = f"{prefix}.{key}" if prefix else key
                items.append((node_id, label))
                if prefix:
                    parent = hashlib.sha1(f"{name}:{prefix}".encode("utf-8")).hexdigest()[:10]
                    lines.append(f"n{parent} --> n{node_id}")
                walk(label, val)

    walk("", data)
    for node_id, label in sorted(items, key=lambda pair: pair[1]):
        lines.append(f"n{node_id}[\"{label}\"]:::node")

    return NEWLINE.join(lines) + NEWLINE


def render(input_dir: Path = Path("docs/source/data"), output_root: Path = GENERATED_ROOT) -> list[dict[str, str]]:
    os.environ["TZ"] = "UTC"
    output_root.mkdir(parents=True, exist_ok=True)
    DIAGRAM_ROOT.mkdir(parents=True, exist_ok=True)

    manifest_entries: list[dict[str, str]] = []

    for input_file in sorted(input_dir.glob("*.yml"), key=lambda p: p.name):
        raw = _load_yaml(input_file)
        normalized = _normalize_value(raw)

        json_output = output_root / f"{input_file.stem}.json"
        yaml_output = output_root / f"{input_file.stem}.normalized.yml"
        diagram_output = DIAGRAM_ROOT / f"{input_file.stem}.mmd"

        json_content = _dump_json(json_output, normalized)
        yaml_content = yaml.safe_dump(
            normalized,
            allow_unicode=True,
            sort_keys=True,
            default_flow_style=False,
        )
        yaml_output.write_text(_normalize_string(yaml_content), encoding="utf-8", newline=NEWLINE)

        diagram = _diagram_from_normalized(input_file.stem, normalized)
        diagram_output.write_text(diagram, encoding="utf-8", newline=NEWLINE)

        manifest_entries.extend(
            [
                {"input": str(input_file), "output": str(json_output), "sha256": _sha256_text(json_content)},
                {"input": str(input_file), "output": str(yaml_output), "sha256": _sha256_text(yaml_output.read_text(encoding='utf-8'))},
                {"input": str(input_file), "output": str(diagram_output), "sha256": _sha256_text(diagram)},
            ]
        )

    manifest = {
        "normalization": {
            "unicode": "NFC",
            "newlines": "LF",
            "date_format": "YYYY-MM-DD",
            "key_order": "lexicographic by normalized key",
        },
        "artifacts": sorted(manifest_entries, key=lambda entry: entry["output"]),
    }
    _dump_json(MANIFEST_PATH, manifest)
    return manifest_entries


def main() -> int:
    parser = argparse.ArgumentParser(description="Render deterministic source-doc generated outputs.")
    parser.add_argument("--input-dir", default="docs/source/data")
    parser.add_argument("--output-dir", default=str(GENERATED_ROOT))
    args = parser.parse_args()
    render(Path(args.input_dir), Path(args.output_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
