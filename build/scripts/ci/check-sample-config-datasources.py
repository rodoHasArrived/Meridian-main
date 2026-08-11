#!/usr/bin/env python3
"""Guard: every provider named in the shipped sample config must be a real DataSourceKind.

`config/appsettings.sample.json` is the file operators copy when they first configure Meridian.
It previously advertised a `StockSharp` data source that is not a `DataSourceKind` member, so
`DataSourceKindConverter.Read` — which fails closed on unknown values — threw a `JsonException`
at startup for anyone who followed it. The same block shipped `Rithmic.Password` and
`CQG.Password` fields, teaching users to put live broker credentials in a JSON file that the
sample's own banner tells them never to hold secrets.

This gate keeps the sample honest in both directions:

- every provider value the sample names (``DataSource`` and each ``DataSources.Sources[].Provider``)
  must exist in ``src/Meridian.Core/Config/DataSourceKind.cs``;
- the sample must not carry password/secret-shaped keys with the credential guidance it disclaims.

Run: ``python3 build/scripts/ci/check-sample-config-datasources.py``
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SAMPLE = REPO_ROOT / "config" / "appsettings.sample.json"
KIND_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "DataSourceKind.cs"

# Keys whose presence in the sample would teach operators to store a live secret in plaintext.
SECRET_KEY_PATTERN = re.compile(r"^(password|secret|apikey|api_key|token|privatekey)$", re.IGNORECASE)


def strip_jsonc(raw: str) -> str:
    """Remove // line comments that sit outside string literals, then trailing commas."""
    out: list[str] = []
    for line in raw.splitlines():
        in_string = False
        escaped = False
        cut: int | None = None
        for index, char in enumerate(line):
            if escaped:
                escaped = False
                continue
            if char == "\\":
                escaped = True
                continue
            if char == '"':
                in_string = not in_string
                continue
            if not in_string and char == "/" and index + 1 < len(line) and line[index + 1] == "/":
                cut = index
                break
        out.append(line if cut is None else line[:cut])
    return re.sub(r",(\s*[}\]])", r"\1", "\n".join(out))


def declared_kinds() -> set[str]:
    text = KIND_SOURCE.read_text(encoding="utf-8")
    body = text[text.index("enum DataSourceKind") :]
    return set(re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*=\s*\d+\s*,?\s*$", body, re.MULTILINE))


def walk_secret_keys(node: object, path: str = "") -> list[str]:
    """Report secret-shaped keys regardless of value.

    The Rithmic and CQG password fields this guard exists to keep out were **empty strings**:
    the harm was the shape of the sample teaching operators to type a live credential into
    JSON, not the placeholder value. Requiring a non-empty value would let the exact regression
    this guard was written for pass.
    """
    findings: list[str] = []
    if isinstance(node, dict):
        for key, value in node.items():
            here = f"{path}.{key}" if path else key
            if SECRET_KEY_PATTERN.match(key) and not isinstance(value, (dict, list)):
                findings.append(here)
            findings.extend(walk_secret_keys(value, here))
    elif isinstance(node, list):
        for i, value in enumerate(node):
            findings.extend(walk_secret_keys(value, f"{path}[{i}]"))
    return findings


def main() -> int:
    if not SAMPLE.exists():
        print(f"error: {SAMPLE} not found", file=sys.stderr)
        return 1

    kinds = declared_kinds()
    if not kinds:
        print(f"error: no enum members parsed from {KIND_SOURCE}", file=sys.stderr)
        return 1

    document = json.loads(strip_jsonc(SAMPLE.read_text(encoding="utf-8")))
    errors: list[str] = []

    named: list[tuple[str, str]] = []
    if isinstance(document.get("DataSource"), str):
        named.append(("DataSource", document["DataSource"]))
    for index, source in enumerate(document.get("DataSources", {}).get("Sources", []) or []):
        provider = source.get("Provider")
        if isinstance(provider, str):
            named.append((f"DataSources.Sources[{index}].Provider", provider))

    for where, value in named:
        if value not in kinds:
            errors.append(
                f"{where} = {value!r} is not a DataSourceKind member "
                f"(valid: {', '.join(sorted(kinds))}). DataSourceKindConverter fails closed on "
                f"unknown values, so this sample would throw at startup."
            )

    for path in walk_secret_keys(document):
        errors.append(
            f"{path} is a secret-shaped key; the sample must not carry credential fields at all, "
            f"empty placeholder or not. Document an environment variable instead."
        )

    if errors:
        print("sample config validation: FAILED", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        f"sample config validation: pass "
        f"({len(named)} provider reference(s) checked against {len(kinds)} DataSourceKind members)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
