from __future__ import annotations

import unittest

from tools.schema_control.policies import evaluate_policies


class SchemaControlPolicyTests(unittest.TestCase):
    def test_unknown_policy_rule_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "unknown-rule"):
            evaluate_policies(
                {"schemas": []},
                {"migration_sets": []},
                {"rules": {"unknown-rule": {"severity": "error"}}},
            )

    def test_primary_key_policy_reports_missing_key_and_honors_exclusion(self) -> None:
        catalog = {
            "schemas": [
                {
                    "name": "accounting",
                    "relations": [
                        {
                            "name": "entry",
                            "kind": "table",
                            "constraints": [
                                {
                                    "name": "pk_entry",
                                    "type": "p",
                                    "columns": ["entry_id"],
                                }
                            ],
                        },
                        {"name": "posting_batch", "kind": "partitioned_table"},
                        {"name": "schema_migrations", "kind": "table"},
                        {"name": "entry_summary", "kind": "view"},
                    ],
                }
            ]
        }
        policies = {
            "rules": {
                "table-must-have-primary-key": {
                    "severity": "error",
                    "exclude": ["*.schema_migrations"],
                }
            }
        }

        result = evaluate_policies(catalog, {}, policies)

        self.assertEqual(
            ["accounting.posting_batch"],
            [item["object"] for item in result["findings"]],
        )
        self.assertTrue(result["failed"])
        self.assertEqual(1, result["counts"]["error"])

    def test_foreign_key_policy_requires_ordered_left_prefix(self) -> None:
        catalog = {
            "tables": [
                {
                    "schema": "positions",
                    "name": "position_lot",
                    "primary_key": {"columns": ["position_lot_id"]},
                    "foreign_keys": [
                        {"name": "fk_book", "columns": ["book_id"]},
                        {
                            "name": "fk_instrument_scope",
                            "columns": ["instrument_id", "scope_id"],
                        },
                    ],
                    "indexes": [
                        {"name": "ix_book_time", "columns": ["book_id", "as_of_time"]},
                        {
                            "name": "ix_scope_instrument",
                            "columns": ["scope_id", "instrument_id"],
                        },
                    ],
                }
            ]
        }
        policies = {"rules": {"foreign-key-must-be-indexed": {"severity": "error"}}}

        result = evaluate_policies(catalog, {}, policies)

        self.assertEqual(1, result["counts"]["error"])
        finding = result["findings"][0]
        self.assertEqual(
            "positions.position_lot.fk_instrument_scope", finding["object"]
        )
        self.assertEqual(["instrument_id", "scope_id"], finding["details"]["columns"])

    def test_foreign_key_policy_honors_constraint_exclusion(self) -> None:
        catalog = {
            "tables": [
                {
                    "schema": "reporting",
                    "name": "legacy_row",
                    "foreign_keys": [{"name": "fk_legacy", "columns": ["legacy_id"]}],
                    "indexes": [],
                }
            ]
        }
        policies = {
            "rules": {
                "foreign-key-must-be-indexed": {
                    "exclude": ["reporting.legacy_row.fk_legacy"],
                }
            }
        }

        result = evaluate_policies(catalog, {}, policies)

        self.assertEqual([], result["findings"])
        self.assertFalse(result["failed"])

    def test_business_table_comment_policy_accepts_comment_and_excludes_ledger(
        self,
    ) -> None:
        catalog = {
            "schema": "ledger",
            "tables": [
                {"name": "entry", "comment": "Immutable posted ledger entry."},
                {"name": "journal"},
                {"name": "ledger_journal_schema_migrations"},
            ],
        }
        policies = {
            "rules": {
                "business-table-must-have-comment": {
                    "severity": "warning",
                    "exclude": ["*.ledger_journal_schema_migrations"],
                }
            }
        }

        result = evaluate_policies(catalog, {}, policies)

        self.assertEqual(
            ["ledger.journal"], [item["object"] for item in result["findings"]]
        )
        self.assertEqual(1, result["counts"]["warning"])
        self.assertFalse(result["failed"])

    def test_rls_policy_applies_only_to_included_schemas(self) -> None:
        catalog = {
            "schemas": [
                {
                    "name": "identity_access",
                    "tables": [
                        {"name": "assignment", "rls_enabled": False},
                        {"name": "principal", "rls_enabled": True},
                    ],
                },
                {
                    "name": "reporting",
                    "tables": [{"name": "public_metric", "rls_enabled": False}],
                },
            ]
        }
        policies = {
            "rules": {
                "schema-rls-required": {
                    "severity": "warning",
                    "include_schemas": ["identity_*"],
                }
            }
        }

        result = evaluate_policies(catalog, {}, policies)

        self.assertEqual(
            ["identity_access.assignment"],
            [item["object"] for item in result["findings"]],
        )
        self.assertFalse(result["failed"])

    def test_no_public_schema_reports_used_objects_but_not_empty_schema(self) -> None:
        catalog = {
            "schemas": [
                {"name": "public", "tables": [{"name": "unowned_table"}]},
                {"name": "accounting", "tables": [{"name": "entry"}]},
            ]
        }
        policies = {"rules": {"no-public-schema": {"severity": "error"}}}

        result = evaluate_policies(catalog, {}, policies)
        empty_result = evaluate_policies(
            {"schemas": [{"name": "public", "tables": []}]}, {}, policies
        )

        self.assertEqual(
            ["public.unowned_table"], [item["object"] for item in result["findings"]]
        )
        self.assertTrue(result["failed"])
        self.assertEqual([], empty_result["findings"])

    def test_legacy_reapply_policy_warns_and_preserves_existing_migration_severity(
        self,
    ) -> None:
        migration_manifest = {
            "migration_sets": [
                {
                    "id": "ledger",
                    "schema": "ledger",
                    "directory": "src/Meridian.Storage/Ledger/Migrations",
                    "drift_policy": "reapply",
                },
                {"id": "banking", "schema": "banking", "drift_policy": "throw"},
            ],
            "findings": [
                {
                    "rule_id": "migration-directory-empty",
                    "severity": "warning",
                    "path": "src/Empty/Migrations",
                    "subject": "empty",
                    "message": "Migration directory is empty.",
                },
                {
                    "rule_id": "migration-immutable-file-modified",
                    "severity": "error",
                    "path": "src/Ledger/Migrations/001_init.sql",
                    "message": "Immutable migration changed.",
                },
            ],
        }
        policies = {
            "rules": {
                "legacy-reapply-migration": {"severity": "warning"},
                # This must not override an existing migration finding with the same severity domain.
                "table-must-have-primary-key": {"severity": "warning"},
            }
        }

        result = evaluate_policies({}, migration_manifest, policies)

        self.assertEqual(1, result["counts"]["error"])
        self.assertEqual(2, result["counts"]["warning"])
        immutable = next(
            item
            for item in result["findings"]
            if item["rule_id"] == "migration-immutable-file-modified"
        )
        self.assertEqual("error", immutable["severity"])
        self.assertEqual("src/Ledger/Migrations/001_init.sql", immutable["object"])
        self.assertEqual(
            "src/Ledger/Migrations/001_init.sql", immutable["details"]["path"]
        )
        self.assertTrue(result["failed"])

    def test_legacy_reapply_policy_honors_exclusion(self) -> None:
        migration_manifest = {
            "migration_sets": [
                {"id": "legacy", "schema": "legacy", "drift_policy": "reapply"}
            ]
        }
        policies = {"rules": {"legacy-reapply-migration": {"exclude": ["legacy"]}}}

        result = evaluate_policies({}, migration_manifest, policies)

        self.assertEqual([], result["findings"])

    def test_findings_have_fixed_fields_and_report_is_deterministic(self) -> None:
        first_catalog = {
            "tables": [
                {"schema": "zeta", "name": "second"},
                {"schema": "alpha", "name": "first"},
            ]
        }
        second_catalog = {"tables": list(reversed(first_catalog["tables"]))}
        policies = {
            "rules": {
                "business-table-must-have-comment": {"severity": "warning"},
                "table-must-have-primary-key": {"severity": "error"},
            }
        }

        first = evaluate_policies(first_catalog, {}, policies)
        second = evaluate_policies(second_catalog, {}, policies)

        self.assertEqual(first, second)
        self.assertEqual(first["fingerprint"], second["fingerprint"])
        self.assertEqual(
            [
                ("error", "table-must-have-primary-key", "alpha.first"),
                ("error", "table-must-have-primary-key", "zeta.second"),
                ("warning", "business-table-must-have-comment", "alpha.first"),
                ("warning", "business-table-must-have-comment", "zeta.second"),
            ],
            [
                (item["severity"], item["rule_id"], item["object"])
                for item in first["findings"]
            ],
        )
        for finding in first["findings"]:
            self.assertEqual(
                {"rule_id", "severity", "object", "message", "details"},
                set(finding),
            )

    def test_warning_does_not_fail_but_configured_error_does(self) -> None:
        catalog = {"tables": [{"schema": "accounting", "name": "entry"}]}
        warning = evaluate_policies(
            catalog,
            {},
            {"rules": {"business-table-must-have-comment": {"severity": "warning"}}},
        )
        error = evaluate_policies(
            catalog,
            {},
            {"rules": {"business-table-must-have-comment": {"severity": "error"}}},
        )

        self.assertFalse(warning["failed"])
        self.assertTrue(error["failed"])


if __name__ == "__main__":
    unittest.main()
