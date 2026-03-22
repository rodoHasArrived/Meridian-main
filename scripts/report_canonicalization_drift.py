#!/usr/bin/env python3
import argparse
import json
from pathlib import Path


def load_mapping_keys(path: Path) -> set[str]:
    data = json.loads(path.read_text(encoding="utf-8"))
    keys: set[str] = set()
    for provider, mappings in data.get("mappings", {}).items():
        for raw_key in mappings.keys():
            keys.add(f"{provider.upper()}:{raw_key}")
    return keys


def collect_fixture_usage(fixtures_dir: Path) -> tuple[set[str], set[str]]:
    conditions: set[str] = set()
    venues: set[str] = set()

    for fixture in sorted(fixtures_dir.glob("*.json")):
        data = json.loads(fixture.read_text(encoding="utf-8"))
        raw = data.get("raw", {})
        provider = str(raw.get("source", "")).upper().strip()
        if not provider:
            continue

        for condition in raw.get("conditions", []) or []:
            if condition is not None and str(condition).strip():
                conditions.add(f"{provider}:{condition}")

        venue = raw.get("venue")
        if venue is not None and str(venue).strip():
            venues.add(f"{provider}:{venue}")

    return conditions, venues


def render_markdown(
    used_conditions: set[str],
    used_venues: set[str],
    missing_conditions: set[str],
    missing_venues: set[str],
) -> str:
    lines = ["## Canonicalization Drift Report", ""]
    lines.append(f"- Fixture condition codes covered: {len(used_conditions)}")
    lines.append(f"- Fixture venues covered: {len(used_venues)}")
    lines.append(f"- Missing condition mappings: {len(missing_conditions)}")
    lines.append(f"- Missing venue mappings: {len(missing_venues)}")
    lines.append("")

    if missing_conditions:
        lines.append("### Missing Condition Mappings")
        for item in sorted(missing_conditions):
            lines.append(f"- `{item}`")
        lines.append("")

    if missing_venues:
        lines.append("### Missing Venue Mappings")
        for item in sorted(missing_venues):
            lines.append(f"- `{item}`")
        lines.append("")

    if not missing_conditions and not missing_venues:
        lines.append("No fixture drift detected.")

    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--output")
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    fixtures_dir = repo_root / "tests" / "Meridian.Tests" / "Application" / "Canonicalization" / "Fixtures"
    condition_path = repo_root / "config" / "condition-codes.json"
    venue_path = repo_root / "config" / "venue-mapping.json"

    used_conditions, used_venues = collect_fixture_usage(fixtures_dir)
    mapped_conditions = load_mapping_keys(condition_path)
    mapped_venues = load_mapping_keys(venue_path)

    missing_conditions = used_conditions - mapped_conditions
    missing_venues = used_venues - mapped_venues

    markdown = render_markdown(used_conditions, used_venues, missing_conditions, missing_venues)

    if args.output:
        Path(args.output).write_text(markdown, encoding="utf-8")
    else:
        print(markdown, end="")

    return 1 if missing_conditions or missing_venues else 0


if __name__ == "__main__":
    raise SystemExit(main())
