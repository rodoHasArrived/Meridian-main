#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check freshness of generated AI navigation artifacts.")
    parser.add_argument(
        "--navigation-json",
        type=Path,
        default=Path("docs/ai/generated/repo-navigation.json"),
        help="Path to generated repo-navigation JSON artifact.",
    )
    parser.add_argument(
        "--max-age-days",
        type=int,
        default=14,
        help="Maximum allowed age (days) for the generated artifact.",
    )
    return parser.parse_args()


def parse_generated_at(raw: str) -> datetime:
    if raw.endswith("Z"):
        raw = raw[:-1] + "+00:00"
    return datetime.fromisoformat(raw).astimezone(timezone.utc)


def artifact_age_days(path: Path) -> int:
    payload = json.loads(path.read_text(encoding="utf-8"))
    generated_at_raw = payload.get("generatedAt")
    if not isinstance(generated_at_raw, str) or not generated_at_raw:
        raise ValueError(f"'generatedAt' missing in {path}")
    generated_at = parse_generated_at(generated_at_raw)
    now = datetime.now(timezone.utc)
    return int((now - generated_at).total_seconds() // 86400)


def main() -> int:
    args = parse_args()
    path = args.navigation_json
    if not path.exists():
        print(f"Navigation artifact missing: {path}")
        return 2
    if args.max_age_days < 1:
        print("--max-age-days must be >= 1")
        return 2

    try:
        age_days = artifact_age_days(path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"Failed to evaluate freshness: {exc}")
        return 2

    if age_days > args.max_age_days:
        print(
            f"AI navigation artifact is stale ({age_days} days old; max allowed {args.max_age_days}). "
            "Run build/scripts/docs/generate-ai-navigation.py."
        )
        return 1

    print(f"AI navigation artifact freshness OK ({age_days} days old; max allowed {args.max_age_days}).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
