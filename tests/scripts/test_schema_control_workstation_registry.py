from __future__ import annotations

import json
import unittest
from pathlib import Path

from tests.scripts.test_schema_control_migrations import FakeConnection, FakeCursor
from tools.schema_control.migrations import (
    MigrationApplyError,
    apply_migrations,
    build_migration_inventory,
)
from tools.schema_control.reservations import (
    load_reservation_register,
    reservation_findings,
)


class SchemaControlWorkstationRegistryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(__file__).resolve().parents[2]
        self.config = json.loads((self.root / "database/schema-control.json").read_text())
        self.module = next(
            item for item in self.config["migration_sets"] if item["id"] == "operator-triage"
        )
        self.inventory = build_migration_inventory(
            self.root,
            {
                "migration_sets": [self.module],
                "migration_search_roots": [self.module["directory"]],
            },
        )

    def test_workstation_sql_is_registered_with_real_runner_ledger_and_reservation_names(self) -> None:
        inventory = build_migration_inventory(self.root, self.config)
        self.assertFalse(inventory.has_errors, inventory.findings)
        self.assertEqual(
            ["001_operator_triage.sql"],
            [item.filename for item in inventory.files_for("operator-triage")],
        )
        register = load_reservation_register(self.root, self.config)
        self.assertEqual([], reservation_findings(inventory, register))
        naming = next(
            item for item in register.migration_sets if item.id == "operator-triage"
        )
        self.assertEqual(1, naming.parse_ordinal("001_operator_triage.sql"))
        self.assertEqual("002_example.sql", naming.filename(2, "example"))

        connection = FakeConnection()
        apply_migrations(
            "postgresql://disposable-test", self.inventory, connect=lambda _: connection
        )
        sql = "\n".join(statement for statement, _ in connection.fake_cursor.executions)
        self.assertIn("CREATE TABLE IF NOT EXISTS operator_triage.item_metadata", sql)
        self.assertIn(
            'insert into operator_triage."operator_triage_schema_migrations" '
            '("filename", "checksum")',
            sql,
        )
        self.assertIn('"checksum" text null', sql)
        self.assertNotIn("__SCHEMA__", sql)
        self.assertFalse(self.inventory.migration_sets[0].track_ordinals)
        self.assertEqual("throw", self.module["drift_policy"])

    def test_registered_workstation_migration_still_rejects_applied_checksum_drift(self) -> None:
        connection = FakeConnection(
            FakeCursor(applied_checksums={"001_operator_triage.sql": "changed"})
        )
        with self.assertRaisesRegex(
            MigrationApplyError, "differs from its configured ledger checksum"
        ):
            apply_migrations(
                "postgresql://disposable-test", self.inventory, connect=lambda _: connection
            )
        self.assertTrue(connection.rolled_back)
        self.assertFalse(connection.committed)


if __name__ == "__main__":
    unittest.main()
