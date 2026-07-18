from __future__ import annotations

import unittest

from tools.schema_control.dependencies import build_dependency_manifest


class DependencyManifestTests(unittest.TestCase):
    def test_combines_physical_contract_and_declared_edges_without_equivalence_claim(
        self,
    ) -> None:
        catalog = {
            "schemas": [
                {
                    "name": "ledger",
                    "relations": [
                        {
                            "name": "journal_legs",
                            "kind": "table",
                            "constraints": [
                                {
                                    "name": "fk_journal",
                                    "type": "foreign_key",
                                    "columns": ["journal_entry_id"],
                                    "referenced_schema": "ledger",
                                    "referenced_table": "journal_entries",
                                }
                            ],
                        }
                    ],
                }
            ]
        }
        contracts = {
            "types": [
                {
                    "full_name": "Meridian.Contracts.Ledger.JournalDto",
                    "references": ["Meridian.Contracts.Ledger.LineDto"],
                    "contract_sets": ["ledger"],
                },
                {
                    "full_name": "Meridian.Contracts.Ledger.LineDto",
                    "references": [],
                    "contract_sets": ["ledger"],
                },
            ]
        }
        config = {
            "migration_sets": [{"id": "ledger", "schema": "ledger"}],
            "contract_sets": [{"id": "ledger", "schemas": ["ledger"]}],
            "logical_dependencies": [
                {
                    "from": "ledger.journal_legs",
                    "to": "ledger.journal_entries",
                    "label": "journal_entry_id",
                }
            ],
        }

        first = build_dependency_manifest(catalog, contracts, config)
        second = build_dependency_manifest(catalog, contracts, config)

        self.assertEqual(first, second)
        self.assertEqual(1, first["summary"]["foreign_keys"])
        self.assertEqual(1, first["summary"]["contract_references"])
        self.assertEqual(2, first["summary"]["contract_set_memberships"])
        self.assertEqual(1, first["summary"]["logical_references"])
        mapping = next(
            edge for edge in first["edges"] if edge["kind"] == "module-contract-map"
        )
        self.assertFalse(mapping["structural_equivalence"])
        logical = next(
            edge for edge in first["edges"] if edge["kind"] == "logical-reference"
        )
        self.assertEqual("database:ledger.journal_legs", logical["source"])
        self.assertEqual("database:ledger.journal_entries", logical["target"])
        self.assertEqual(64, len(first["fingerprint"]))


if __name__ == "__main__":
    unittest.main()
