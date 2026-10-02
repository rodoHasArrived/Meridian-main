#!/usr/bin/env python3
"""Normalize standard YAML with deterministic ordering and Unicode composition."""

from __future__ import annotations

import argparse
import datetime as dt
import locale
import os
import re
import sys
import unicodedata
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "build" / "scripts" / "docs"))
from common import load_data

DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def _enforce_environment() -> None:
    os.environ["TZ"] = "UTC"
    import time

    if hasattr(time, "tzset"):
        time.tzset()
    locale.setlocale(locale.LC_ALL, "C")


def _n(value: str) -> str:
    return unicodedata.normalize("NFC", value)


def _strict_date(value: Any) -> None:
    if isinstance(value, dt.date) and not isinstance(value, dt.datetime):
        return
    if not isinstance(value, str) or not DATE_RE.fullmatch(value):
        raise ValueError(f"Date must be YYYY-MM-DD only: {value}")
    dt.date.fromisoformat(value)


def _normalize(value: Any, date_fields: set[str]) -> Any:
    if isinstance(value, dict):
        result = {}
        for raw_key in sorted(value, key=lambda key: _n(str(key))):
            if not isinstance(raw_key, str):
                raise TypeError(f"Non-string key is ambiguous: {raw_key!r}")
            key = _n(raw_key)
            item = _normalize(value[raw_key], date_fields)
            if key in date_fields:
                _strict_date(item)
            result[key] = item
        return result
    if isinstance(value, list):
        return [_normalize(item, date_fields) for item in value]
    if isinstance(value, str):
        return _n(value)
    if value is None or isinstance(value, (bool, int, float, dt.date)):
        return value
    raise TypeError(f"Unsupported scalar type: {type(value)!r}")


def render(input_path: str, output_path: str, date_fields: set[str]) -> None:
    normalized = _normalize(load_data(Path(input_path)), date_fields)
    import yaml

    rendered = yaml.safe_dump(normalized, allow_unicode=True, sort_keys=False)
    Path(output_path).write_text(rendered, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    _enforce_environment()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input")
    parser.add_argument("output")
    parser.add_argument("--date-field", action="append", default=[])
    args = parser.parse_args()
    render(args.input, args.output, set(args.date_field))
