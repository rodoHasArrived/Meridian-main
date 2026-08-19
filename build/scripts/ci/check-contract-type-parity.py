#!/usr/bin/env python3
"""Check that C# workstation DTOs and their TypeScript declarations still agree.

PRD-109 asks for "C#/TypeScript compatibility checks" on the contract types both clients share.
The duplicate-export gate (``check-dashboard-type-barrel.py``) already guarantees each TS name has
exactly one declaration, but nothing compared those declarations to the C# records the API actually
serialises. A renamed, added, or newly-nullable member on the C# side therefore reached the browser
as a silently missing field: the dashboard casts parsed JSON to its interface, so the compiler
cannot notice.

This gate compares an explicitly enumerated registry of record/interface pairs on three axes:

* member sets, comparing C# PascalCase against TS camelCase;
* nullability, where C# ``T?`` must line up with a TS ``?`` or a ``| null`` union;
* collection-ness, where ``IReadOnlyList<T>`` and friends must line up with a TS array.

The registry is deliberately explicit rather than discovered. "Representative" coverage is only
meaningful if it is enumerated, and a fail-closed gate must be able to tell "this pair is known to
be out of scope" from "this pair silently stopped being checked": an entry naming a record or
interface that no longer exists is a failure, not a skip.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_REGISTRY = REPO_ROOT / "build" / "config" / "contracts" / "type-parity-registry.json"

# C# collection types whose TypeScript counterpart is an array.
COLLECTION_PREFIXES = (
    "IReadOnlyList<",
    "IReadOnlyCollection<",
    "IList<",
    "ICollection<",
    "IEnumerable<",
    "List<",
)


def camel_case(name: str) -> str:
    """Match System.Text.Json's default camelCase policy for a PascalCase member."""
    if not name:
        return name
    if len(name) > 1 and name[0].isupper() and name[1].isupper():
        # Runs of capitals lower wholesale until the last one before a lowercase letter,
        # which is what JsonNamingPolicy.CamelCase does (e.g. "IOPath" -> "ioPath").
        index = 0
        while index < len(name) and name[index].isupper():
            if index + 1 < len(name) and not name[index + 1].isupper():
                break
            index += 1
        return name[:index].lower() + name[index:]
    return name[0].lower() + name[1:]


def split_top_level(text: str, separator: str = ",") -> list[str]:
    """Split on a separator that is not nested inside <>, (), [] or a string."""
    parts: list[str] = []
    depth = 0
    current: list[str] = []
    in_string = False
    for char in text:
        if char == '"':
            in_string = not in_string
        if not in_string:
            if char in "<([":
                depth += 1
            elif char in ">)]":
                depth -= 1
            elif char == separator and depth == 0:
                parts.append("".join(current))
                current = []
                continue
        current.append(char)
    tail = "".join(current).strip()
    if tail:
        parts.append(tail)
    return [part.strip() for part in parts if part.strip()]


def _balanced_slice(text: str, start: int, opener: str, closer: str) -> str | None:
    depth = 0
    for index in range(start, len(text)):
        char = text[index]
        if char == opener:
            depth += 1
        elif char == closer:
            depth -= 1
            if depth == 0:
                return text[start + 1 : index]
    return None


def parse_csharp_record(source: str, record_name: str) -> dict[str, dict] | None:
    """Return {member: {'type', 'nullable', 'collection'}} for a positional record."""
    match = re.search(rf"\brecord\s+{re.escape(record_name)}\s*\(", source)
    if match is None:
        return None
    body = _balanced_slice(source, match.end() - 1, "(", ")")
    if body is None:
        return None

    members: dict[str, dict] = {}
    for parameter in split_top_level(body):
        cleaned = re.sub(r"\[[^\]]*\]", "", parameter).strip()
        # Drop a default value; the name is the last identifier before it.
        cleaned = split_top_level(cleaned, "=")[0].strip() if "=" in cleaned else cleaned
        tokens = cleaned.rsplit(" ", 1)
        if len(tokens) != 2:
            continue
        type_text, member = tokens[0].strip(), tokens[1].strip()
        if not member.isidentifier():
            continue
        members[member] = {
            "type": type_text,
            "nullable": type_text.endswith("?"),
            "collection": type_text.rstrip("?").endswith("[]")
            or any(type_text.lstrip().startswith(prefix) for prefix in COLLECTION_PREFIXES),
        }
    return members


def parse_typescript_interface(source: str, interface_name: str) -> dict[str, dict] | None:
    match = re.search(rf"\binterface\s+{re.escape(interface_name)}\s*\{{", source)
    if match is None:
        return None
    body = _balanced_slice(source, match.end() - 1, "{", "}")
    if body is None:
        return None

    members: dict[str, dict] = {}
    for line in split_top_level(body, ";"):
        entry = line.strip()
        if not entry or entry.startswith("//") or entry.startswith("/*"):
            continue
        entry = re.sub(r"//.*$", "", entry, flags=re.MULTILINE).strip()
        member_match = re.match(r"^([A-Za-z_$][\w$]*)(\?)?\s*:\s*(.+)$", entry, flags=re.DOTALL)
        if member_match is None:
            continue
        member, optional, type_text = member_match.groups()
        type_text = " ".join(type_text.split())
        union = [part.strip() for part in split_top_level(type_text, "|")]
        non_null = [part for part in union if part not in {"null", "undefined"}]
        members[member] = {
            "type": type_text,
            "nullable": bool(optional) or len(non_null) != len(union),
            "collection": any(part.endswith("[]") or part.startswith("Array<") for part in non_null),
        }
    return members


def compare(entry: dict, csharp: dict[str, dict], typescript: dict[str, dict]) -> list[str]:
    ignored = set(entry.get("ignore_members", []))
    problems: list[str] = []
    label = f"{entry['csharp_record']} <-> {entry['typescript_interface']}"

    expected = {camel_case(name): data for name, data in csharp.items() if name not in ignored}
    actual = {name: data for name, data in typescript.items() if name not in ignored}

    for name in sorted(set(expected) - set(actual)):
        problems.append(f"{label}: C# member '{name}' has no TypeScript declaration.")
    for name in sorted(set(actual) - set(expected)):
        problems.append(f"{label}: TypeScript member '{name}' has no C# counterpart.")

    for name in sorted(set(expected) & set(actual)):
        want, have = expected[name], actual[name]
        if want["nullable"] != have["nullable"]:
            problems.append(
                f"{label}: '{name}' nullability differs — C# '{want['type']}' vs TypeScript '{have['type']}'."
            )
        if want["collection"] != have["collection"]:
            problems.append(
                f"{label}: '{name}' collection shape differs — C# '{want['type']}' vs TypeScript '{have['type']}'."
            )
    return problems


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--registry", type=Path, default=DEFAULT_REGISTRY)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--json-output", type=Path, default=None)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)

    if not args.registry.is_file():
        print(f"contract-type-parity: registry not found: {args.registry}", file=sys.stderr)
        return 2
    registry = json.loads(args.registry.read_text(encoding="utf-8"))
    entries = registry.get("pairs", [])
    if not entries:
        print("contract-type-parity: registry lists no pairs; the gate would pass vacuously.", file=sys.stderr)
        return 2

    exemptions = {item["typescript_interface"]: item for item in registry.get("known_divergences", [])}

    problems: list[str] = []
    resolved_exemptions: list[str] = []
    exempted: list[str] = []
    checked = 0
    for entry in list(entries) + list(exemptions.values()):
        csharp_path = args.repo_root / entry["csharp_file"]
        typescript_path = args.repo_root / entry["typescript_file"]
        if not csharp_path.is_file():
            problems.append(f"{entry['csharp_record']}: missing C# file {entry['csharp_file']}.")
            continue
        if not typescript_path.is_file():
            problems.append(f"{entry['typescript_interface']}: missing TypeScript file {entry['typescript_file']}.")
            continue

        csharp = parse_csharp_record(csharp_path.read_text(encoding="utf-8"), entry["csharp_record"])
        typescript = parse_typescript_interface(
            typescript_path.read_text(encoding="utf-8"), entry["typescript_interface"]
        )
        if csharp is None:
            problems.append(
                f"{entry['csharp_record']}: no positional record by that name in {entry['csharp_file']}."
            )
            continue
        if typescript is None:
            problems.append(
                f"{entry['typescript_interface']}: no interface by that name in {entry['typescript_file']}."
            )
            continue

        found = compare(entry, csharp, typescript)
        exemption = exemptions.get(entry["typescript_interface"])
        if exemption is not None and exemption is entry:
            # A recorded divergence that has since been repaired must be promoted, or the list
            # rots into a permanent allowance. It is allowed to shrink to empty.
            if found:
                exempted.append(entry["typescript_interface"])
            else:
                resolved_exemptions.append(entry["typescript_interface"])
            continue
        checked += 1
        problems.extend(found)

    if args.json_output is not None:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(
            json.dumps(
                {
                    "pairs": len(entries),
                    "checked": checked,
                    "known_divergences": len(exemptions),
                    "still_diverging": exempted,
                    "resolved_divergences": resolved_exemptions,
                    "problems": problems,
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )

    for name in sorted(resolved_exemptions):
        problems.append(
            f"{name}: recorded in known_divergences but now agrees; move it into 'pairs' so the "
            "parity is enforced from here on."
        )

    if problems:
        print("contract-type-parity: FAIL", file=sys.stderr)
        for problem in problems:
            print(f"- {problem}", file=sys.stderr)
        return 1

    print(f"contract-type-parity: {checked} record/interface pair(s) agree.")
    if exempted:
        print(f"contract-type-parity: {len(exempted)} recorded divergence(s) still open:")
        for name in sorted(exempted):
            print(f"- {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
