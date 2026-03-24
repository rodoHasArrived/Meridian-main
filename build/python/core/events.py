import json
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .utils import colorize, ensure_directory, iso_timestamp


@dataclass
class BuildEvent:
    event_id: str
    timestamp: str
    phase: str
    project: str
    event_type: str
    duration_ms: int | None
    context: dict[str, Any]
    error_code: str | None
    error_message: str | None
    tags: list[str]


class BuildEventEmitter:
    def __init__(self, output_dir: Path, verbosity: str) -> None:
        self.output_dir = output_dir
        self.verbosity = verbosity
        ensure_directory(self.output_dir)
        self.jsonl_path = self.output_dir / "build-events.jsonl"
        self.human_log_path = self.output_dir / "build-events.log"

    def _write_event(self, event: BuildEvent) -> None:
        payload = {
            "event_id": event.event_id,
            "timestamp": event.timestamp,
            "phase": event.phase,
            "project": event.project,
            "event_type": event.event_type,
            "duration_ms": event.duration_ms,
            "context": event.context,
            "error_code": event.error_code,
            "error_message": event.error_message,
            "tags": event.tags,
        }
        with self.jsonl_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(payload) + "\n")
        with self.human_log_path.open("a", encoding="utf-8") as handle:
            handle.write(self._format_human(event) + "\n")

    def _format_human(self, event: BuildEvent) -> str:
        status = event.event_type
        if status == "completed":
            status_label = colorize("SUCCESS", "0;32")
        elif status == "failed":
            status_label = colorize("FAILED", "0;31")
        elif status == "warning":
            status_label = colorize("WARN", "1;33")
        else:
            status_label = colorize("INFO", "0;34")

        duration = ""
        if event.duration_ms is not None:
            duration = f" ({event.duration_ms}ms)"
        context = ""
        if event.context:
            context = f" | {json.dumps(event.context, sort_keys=True)}"
        message = f"[{event.timestamp}] {status_label} {event.phase} {event.project}{duration}{context}"
        if event.error_message:
            message = f"{message} :: {event.error_message}"
        return message

    def emit(
        self,
        phase: str,
        project: str,
        event_type: str,
        duration_ms: int | None = None,
        context: dict[str, Any] | None = None,
        error_code: str | None = None,
        error_message: str | None = None,
        tags: list[str] | None = None,
    ) -> None:
        event = BuildEvent(
            event_id=str(uuid.uuid4()),
            timestamp=iso_timestamp(),
            phase=phase,
            project=project,
            event_type=event_type,
            duration_ms=duration_ms,
            context=context or {},
            error_code=error_code,
            error_message=error_message,
            tags=tags or [],
        )
        self._write_event(event)
        if self._should_print(event_type):
            print(self._format_human(event))

    def _should_print(self, event_type: str) -> bool:
        if self.verbosity == "quiet":
            return event_type in {"failed", "warning"}
        if self.verbosity == "normal":
            return True
        if self.verbosity in {"verbose", "debug"}:
            return True
        return True
