import sqlite3
from dataclasses import dataclass
from pathlib import Path


@dataclass
class BuildRecord:
    fingerprint: str
    timestamp: str
    duration_ms: int
    success: bool
    git_sha: str
    git_branch: str
    warnings_count: int
    errors_count: int
    configuration: str


class BuildHistory:
    def __init__(self, db_path: Path) -> None:
        self.db_path = db_path
        self._init_db()

    def _init_db(self) -> None:
        with sqlite3.connect(self.db_path) as conn:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS builds (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fingerprint TEXT,
                    timestamp TEXT,
                    duration_ms INTEGER,
                    success INTEGER,
                    git_sha TEXT,
                    git_branch TEXT,
                    warnings_count INTEGER,
                    errors_count INTEGER,
                    configuration TEXT
                )
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS build_phases (
                    build_id INTEGER,
                    phase TEXT,
                    duration_ms INTEGER,
                    success INTEGER,
                    cache_hit INTEGER
                )
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS build_errors (
                    build_id INTEGER,
                    error_code TEXT,
                    error_message TEXT,
                    phase TEXT
                )
                """
            )

    def record_build(self, record: BuildRecord) -> int:
        with sqlite3.connect(self.db_path) as conn:
            cursor = conn.execute(
                """
                INSERT INTO builds (
                    fingerprint, timestamp, duration_ms, success, git_sha, git_branch,
                    warnings_count, errors_count, configuration
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    record.fingerprint,
                    record.timestamp,
                    record.duration_ms,
                    1 if record.success else 0,
                    record.git_sha,
                    record.git_branch,
                    record.warnings_count,
                    record.errors_count,
                    record.configuration,
                ),
            )
            return cursor.lastrowid

    def record_phase(self, build_id: int, phase: str, duration_ms: int, success: bool) -> None:
        with sqlite3.connect(self.db_path) as conn:
            conn.execute(
                """
                INSERT INTO build_phases (build_id, phase, duration_ms, success, cache_hit)
                VALUES (?, ?, ?, ?, ?)
                """,
                (build_id, phase, duration_ms, 1 if success else 0, 0),
            )

    def average_duration(self) -> float | None:
        with sqlite3.connect(self.db_path) as conn:
            row = conn.execute("SELECT AVG(duration_ms) FROM builds").fetchone()
        if row and row[0]:
            return float(row[0])
        return None

    def recent_builds(self, limit: int = 10) -> list[tuple]:
        with sqlite3.connect(self.db_path) as conn:
            return conn.execute(
                "SELECT timestamp, duration_ms, success FROM builds ORDER BY id DESC LIMIT ?",
                (limit,),
            ).fetchall()
