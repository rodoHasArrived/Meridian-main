from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tools.schema_control.common import (
    Finding,
    canonical_json,
    fingerprint,
    normalize_text,
)
from tools.schema_control.migrations import (
    MigrationApplyError,
    apply_migrations,
    build_migration_inventory,
    compare_immutable_migrations,
    detect_destructive_changes,
)


class FakeCursor:
    def __init__(
        self,
        *,
        applied_checksums: dict[str, str] | None = None,
        fail_when_sql_contains: str | None = None,
        existing_object: tuple[str, str] | None = None,
    ) -> None:
        self.applied_checksums = applied_checksums or {}
        self.fail_when_sql_contains = fail_when_sql_contains
        self.existing_object = existing_object
        self.executions: list[tuple[str, tuple[object, ...] | None]] = []
        self._next_row: tuple[str] | None = None
        self.closed = False

    def execute(self, sql: str, parameters: tuple[object, ...] | None = None) -> None:
        self.executions.append((sql, parameters))
        if self.fail_when_sql_contains and self.fail_when_sql_contains in sql:
            raise RuntimeError("synthetic database failure")
        if "schema-control disposable database guard" in sql:
            self._next_row = self.existing_object
            return
        if sql.lstrip().lower().startswith("select") and parameters:
            checksum = self.applied_checksums.get(str(parameters[0]))
            self._next_row = None if checksum is None else (checksum,)

    def fetchone(self) -> tuple[str] | None:
        return self._next_row

    def close(self) -> None:
        self.closed = True


class FakeConnection:
    def __init__(self, cursor: FakeCursor | None = None) -> None:
        self.fake_cursor = cursor or FakeCursor()
        self.committed = False
        self.rolled_back = False
        self.closed = False

    def cursor(self) -> FakeCursor:
        return self.fake_cursor

    def commit(self) -> None:
        self.committed = True

    def rollback(self) -> None:
        self.rolled_back = True

    def close(self) -> None:
        self.closed = True


class SchemaControlMigrationTests(unittest.TestCase):
    def test_common_helpers_normalize_unicode_sort_json_and_use_lf(self) -> None:
        value = {"z": "Cafe\u0301\r\nnext", "a": {"b": 2, "a": 1}}

        rendered = canonical_json(value)

        self.assertTrue(rendered.endswith("\n"))
        self.assertNotIn("\r", rendered)
        self.assertIn("Café", rendered)
        self.assertLess(rendered.index('"a"'), rendered.index('"z"'))
        self.assertEqual(json.loads(rendered)["z"], "Café\nnext")
        self.assertEqual(
            fingerprint(value), fingerprint({"a": {"a": 1, "b": 2}, "z": "Café\nnext"})
        )
        self.assertEqual(
            "example-rule", Finding("example-rule", "warning", "Example").code
        )
        self.assertEqual("Café\n", normalize_text("Cafe\u0301\r\n"))

    def test_inventory_is_sorted_and_allows_repeated_prefixes_when_ordinals_are_not_tracked(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_z.sql", "select 2;")
            self._write(root, "src/Feature/Migrations/001_a.sql", "select 1;")

            inventory = build_migration_inventory(
                root, self._config(track_ordinals=False)
            )

        self.assertFalse(inventory.has_errors)
        self.assertEqual(
            ["001_a.sql", "001_z.sql"], [item.filename for item in inventory.files]
        )
        self.assertTrue(all(item.ordinal is None for item in inventory.files))

    def test_inventory_enforces_unique_ordinals_only_when_configured(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_alpha.sql", "select 1;")
            self._write(root, "src/Feature/Migrations/001_beta.sql", "select 2;")

            inventory = build_migration_inventory(
                root, self._config(track_ordinals=True)
            )

        self.assertTrue(inventory.has_errors)
        self.assertEqual(
            ["migration-ordinal-duplicate"],
            [
                finding.rule_id
                for finding in inventory.findings
                if finding.rule_id == "migration-ordinal-duplicate"
            ],
        )

    def test_inventory_reports_unregistered_sql_migration_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_feature.sql", "select 1;")
            self._write(root, "src/Other/Migrations/001_other.sql", "select 2;")

            inventory = build_migration_inventory(root, self._config())

        finding = next(
            item
            for item in inventory.findings
            if item.rule_id == "migration-directory-unregistered"
        )
        self.assertEqual("src/Other/Migrations", finding.path)

    def test_immutable_comparison_flags_changed_existing_file_but_not_added_file(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(
                root, "src/Feature/Migrations/001_existing.sql", "select 'current';"
            )
            self._write(root, "src/Feature/Migrations/002_added.sql", "select 'new';")
            inventory = build_migration_inventory(root, self._config())
            baseline = {
                "src/Feature/Migrations/001_existing.sql": b"select 'baseline';",
            }

            findings = compare_immutable_migrations(inventory, baseline.get)

        self.assertEqual(1, len(findings))
        self.assertEqual("migration-immutable-file-modified", findings[0].rule_id)
        self.assertTrue(findings[0].path.endswith("001_existing.sql"))

    def test_destructive_scan_checks_added_files_and_ignores_comments(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(
                root,
                "src/Feature/Migrations/001_existing.sql",
                "alter table __SCHEMA__.book drop column legacy;",
            )
            self._write(
                root,
                "src/Feature/Migrations/002_added.sql",
                "-- drop table ignored;\n/* truncate ignored; */\nalter table __SCHEMA__.book rename column old to new;\ndrop policy legacy_access on __SCHEMA__.book;",
            )
            inventory = build_migration_inventory(root, self._config())
            baseline = {
                "src/Feature/Migrations/001_existing.sql": b"baseline content is intentionally irrelevant",
            }

            findings = detect_destructive_changes(inventory, baseline.get)

        self.assertEqual(
            ["migration-drop-object", "migration-rename"],
            [item.rule_id for item in findings],
        )
        self.assertTrue(findings[0].path.endswith("002_added.sql"))

    def test_apply_migrations_uses_injected_connector_schema_substitution_and_ledger(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(
                root,
                "src/Feature/Migrations/001_create.sql",
                "create table __SCHEMA__.records (id bigint primary key);",
            )
            inventory = build_migration_inventory(
                root, self._config(track_ordinals=True)
            )
            connection = FakeConnection()

            result = apply_migrations(
                "postgresql://schema-test",
                inventory,
                connect=lambda _: connection,
            )

        rendered_sql = "\n".join(sql for sql, _ in connection.fake_cursor.executions)
        self.assertIn("create schema if not exists test_schema", rendered_sql.lower())
        self.assertIn("create table test_schema.records", rendered_sql.lower())
        self.assertNotIn("__SCHEMA__", rendered_sql)
        self.assertTrue(
            any(
                'insert into test_schema."schema_migrations"' in sql.lower()
                for sql, _ in connection.fake_cursor.executions
            )
        )
        self.assertEqual(("src/Feature/Migrations/001_create.sql",), result.applied)
        self.assertEqual((), result.skipped)
        self.assertTrue(connection.committed)
        self.assertFalse(connection.rolled_back)
        self.assertTrue(connection.closed)
        self.assertTrue(connection.fake_cursor.closed)

    def test_apply_migrations_refuses_nonempty_database_before_writes(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_create.sql", "select 1;")
            inventory = build_migration_inventory(root, self._config())
            connection = FakeConnection(
                FakeCursor(existing_object=("schema", "production"))
            )

            with self.assertRaisesRegex(
                MigrationApplyError,
                "requires a disposable, empty PostgreSQL database",
            ):
                apply_migrations(
                    "postgresql://schema-test",
                    inventory,
                    connect=lambda _: connection,
                )

        executed = "\n".join(sql for sql, _ in connection.fake_cursor.executions)
        self.assertNotIn("create schema", executed.lower())
        self.assertFalse(connection.committed)
        self.assertTrue(connection.rolled_back)
        self.assertTrue(connection.closed)

    def test_ledger_shape_matches_configured_checksum_nullability_and_ordinal_name(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_create.sql", "select 1;")
            config = self._config(track_ordinals=True)
            ledger = config["migration_sets"][0]["ledger"]
            ledger["ordinal_column"] = "migration_ordinal"
            ledger["checksum_required"] = True
            inventory = build_migration_inventory(root, config)
            connection = FakeConnection()

            apply_migrations(
                "postgresql://schema-test",
                inventory,
                connect=lambda _: connection,
            )

        rendered_sql = "\n".join(
            sql for sql, _ in connection.fake_cursor.executions
        ).lower()
        self.assertIn('"migration_ordinal" integer not null', rendered_sql)
        self.assertIn('"checksum" text not null', rendered_sql)
        self.assertIn(
            '("migration_ordinal", "filename", "checksum")',
            rendered_sql,
        )

    def test_apply_migrations_fails_closed_on_inventory_errors_without_connecting(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(root, "src/Feature/Migrations/001_a.sql", "select 1;")
            self._write(root, "src/Feature/Migrations/001_b.sql", "select 2;")
            inventory = build_migration_inventory(
                root, self._config(track_ordinals=True)
            )
            connected = False

            def connect(_: str) -> FakeConnection:
                nonlocal connected
                connected = True
                return FakeConnection()

            with self.assertRaises(MigrationApplyError):
                apply_migrations("postgresql://schema-test", inventory, connect=connect)

        self.assertFalse(connected)

    def test_apply_migrations_rolls_back_and_closes_connection_on_database_failure(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write(
                root,
                "src/Feature/Migrations/001_create.sql",
                "create table __SCHEMA__.records (id bigint primary key);",
            )
            inventory = build_migration_inventory(root, self._config())
            connection = FakeConnection(
                FakeCursor(fail_when_sql_contains="create table test_schema.records")
            )

            with self.assertRaisesRegex(RuntimeError, "synthetic database failure"):
                apply_migrations(
                    "postgresql://schema-test",
                    inventory,
                    connect=lambda _: connection,
                )

        self.assertFalse(connection.committed)
        self.assertTrue(connection.rolled_back)
        self.assertTrue(connection.closed)
        self.assertTrue(connection.fake_cursor.closed)

    @staticmethod
    def _write(root: Path, relative_path: str, content: str) -> None:
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")

    @staticmethod
    def _config(*, track_ordinals: bool = False) -> dict[str, object]:
        return {
            "migration_sets": [
                {
                    "id": "feature",
                    "directory": "src/Feature/Migrations",
                    "schema": "test_schema",
                    "track_ordinals": track_ordinals,
                    "ledger": {
                        "table": "schema_migrations",
                        "key_column": "filename",
                        "checksum_column": "checksum",
                    },
                }
            ],
            "migration_search_roots": ["src"],
        }


if __name__ == "__main__":
    unittest.main()
