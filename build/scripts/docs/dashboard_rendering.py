"""Shared helpers for dashboard generators.

JSON is the canonical source of truth. Dashboards should generate JSON first,
then render Markdown from that JSON payload to avoid drift between formats.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def current_utc_timestamp() -> str:
    """Return an ISO-8601 UTC timestamp string."""
    return datetime.now(timezone.utc).isoformat()


def write_canonical_json(payload: dict[str, Any], json_output: Path) -> dict[str, Any]:
    """Write canonical JSON payload and return the same payload."""
    json_output.parent.mkdir(parents=True, exist_ok=True)
    json_output.write_text(json.dumps(payload, indent=2, default=str) + "\n", encoding="utf-8")
    return payload


def load_canonical_json(json_output: Path) -> dict[str, Any]:
    """Load canonical JSON payload from disk."""
    return json.loads(json_output.read_text(encoding="utf-8"))


def render_markdown_from_json(
    *,
    json_payload: dict[str, Any],
    render_body: callable,
    data_sources: list[str],
) -> str:
    """Render deterministic markdown using canonical JSON payload.

    ``render_body`` should accept the parsed JSON payload and return markdown body
    content for the specific dashboard.
    """
    lines = [
        "> Auto-generated from canonical JSON payload.",
        f"> Generated: {json_payload.get('generated_at', current_utc_timestamp())}",
        f"> Data sources: {', '.join(data_sources)}",
        "",
    ]
    lines.append(render_body(json_payload).rstrip())
    lines.append("")
    return "\n".join(lines)
