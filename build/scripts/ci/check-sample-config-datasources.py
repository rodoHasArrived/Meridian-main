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
MASKER_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "SensitiveValueMasker.cs"
STREAMING_SOURCE = (
    REPO_ROOT / "src" / "Meridian.Application" / "Composition" / "Features"
    / "ProviderFeatureRegistration.Registry.cs"
)

# Fallback vocabulary if the masker cannot be parsed. The live list is read from
# SensitiveValueMasker so this guard and the runtime redaction share one definition of "secret".
FALLBACK_SECRET_NAMES = frozenset(
    {
        "KeyId", "SecretKey", "ApiKey", "Token", "Password", "Secret",
        "Credentials", "ConnectionString", "AccessKey", "BearerToken",
        "ClientSecret", "PrivateKey", "Certificate",
    }
)


def secret_key_names() -> frozenset[str]:
    """Mirror ``SensitiveValueMasker.SensitivePropertyNames``.

    An anchored pattern over a hand-written shortlist missed compound names the repository
    already treats as secret — ``SecretKey``, ``ClientSecret``, ``BearerToken``, ``AccessKey``.
    Reading the masker keeps one vocabulary instead of two that drift.
    """
    try:
        text = MASKER_SOURCE.read_text(encoding="utf-8")
        block = text[text.index("SensitivePropertyNames") :]
        block = block[: block.index("};")]
        names = {name for name in re.findall(r'"([A-Za-z]+)"', block)}
        return frozenset(names or FALLBACK_SECRET_NAMES)
    except (OSError, ValueError):
        return FALLBACK_SECRET_NAMES


def streaming_source_ids() -> frozenset[str]:
    """Factory ids registered for real-time streaming.

    Enum membership is necessary but not sufficient for the top-level ``DataSource``: Yahoo is a
    defined ``DataSourceKind`` yet has no streaming factory, so selecting it fails at collector
    startup. Advertising it as a primary real-time source is exactly the trap this guard exists
    to stop.
    """
    try:
        text = STREAMING_SOURCE.read_text(encoding="utf-8")
        return frozenset(re.findall(r'RegisterStreamingFactory\(\s*"([^"]+)"', text))
    except OSError:
        return frozenset()


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


def declared_kinds() -> dict[str, int]:
    """Return the enum's member names mapped to their numeric values.

    Both forms matter: ``DataSourceKindConverter`` accepts a string name *and* a JSON number
    (``reader.TokenType == JsonTokenType.Number``), so a sample carrying ``"DataSource": 99``
    fails at startup exactly like an unknown name.
    """
    text = KIND_SOURCE.read_text(encoding="utf-8")
    body = text[text.index("enum DataSourceKind") :]
    pairs = re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+)\s*,?\s*$", body, re.MULTILINE)
    return {name: int(value) for name, value in pairs}


SECRET_KEYS_FOLDED = frozenset(name.casefold() for name in secret_key_names())


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
            if key.casefold() in SECRET_KEYS_FOLDED and not isinstance(value, (dict, list)):
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

    named: list[tuple[str, object]] = []
    if "DataSource" in document:
        named.append(("DataSource", document["DataSource"]))
    for index, source in enumerate(document.get("DataSources", {}).get("Sources", []) or []):
        if "Provider" in source:
            named.append((f"DataSources.Sources[{index}].Provider", source["Provider"]))

    valid = (
        f"names: {', '.join(sorted(kinds))}; numbers: "
        f"{', '.join(str(v) for v in sorted(kinds.values()))}"
    )
    folded = {name.casefold(): name for name in kinds}
    by_number = {value: name for name, value in kinds.items()}

    def resolve(value: object) -> tuple[str | None, str | None]:
        """Resolve a sample value the way DataSourceKindConverter does.

        The converter uses case-insensitive ``Enum.TryParse`` and also accepts a JSON number or a
        numeric string, rejecting undefined values via ``Enum.IsDefined``. Matching those exact
        semantics matters in both directions: rejecting ``"synthetic"`` or ``"5"`` would fail the
        gate for a config the application loads perfectly well.
        """
        # bool is a subclass of int in Python; a JSON true/false is never a valid enum value.
        if isinstance(value, bool) or not isinstance(value, (str, int)):
            return None, f"is neither a name nor a number, so the converter cannot read it ({valid})"
        if isinstance(value, int):
            name = by_number.get(value)
            return (name, None) if name else (None, f"is not a defined DataSourceKind value ({valid})")
        text = value.strip()
        if text.lstrip("+-").isdigit():
            name = by_number.get(int(text))
            return (
                (name, None)
                if name
                else (None, f"is a numeric string outside the defined DataSourceKind values ({valid})")
            )
        name = folded.get(text.casefold())
        return (name, None) if name else (None, f"is not a DataSourceKind member ({valid})")

    streaming = streaming_source_ids()
    for where, value in named:
        name, problem = resolve(value)
        if problem is not None:
            errors.append(
                f"{where} = {value!r} {problem}. DataSourceKindConverter fails closed, so this "
                f"sample would throw at startup."
            )
            continue
        # The top-level selector chooses the real-time client. Enum membership is not enough:
        # a kind with no streaming factory is rejected at collector startup.
        if where == "DataSource" and streaming and name is not None:
            if name.casefold() not in {sid.casefold() for sid in streaming}:
                errors.append(
                    f"{where} = {value!r} resolves to {name}, which has no streaming factory "
                    f"(registered: {', '.join(sorted(streaming))}). It cannot be a primary "
                    f"real-time source; document it under backfill instead."
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
