from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.schema_control.render import render_schema_mermaid, render_snapshot


class SchemaRenderTests(unittest.TestCase):
    def test_large_contract_set_is_paginated_without_omitting_objects(self) -> None:
        contracts = {
            "types": [
                {
                    "full_name": f"Meridian.Contracts.Ledger.Dto{index:03d}",
                    "contract_sets": ["ledger-contracts"],
                    "members": [],
                    "references": [],
                }
                for index in range(81)
            ]
        }
        config = {
            "migration_sets": [],
            "contract_sets": [
                {
                    "id": "ledger-contracts",
                    "schemas": ["ledger"],
                    "diagram": True,
                }
            ],
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            render_snapshot(
                root,
                catalog={"schemas": []},
                migrations={"migration_sets": [], "files": []},
                contracts=contracts,
                dependencies={"nodes": [], "edges": []},
                policy_report={"failed": False, "findings": []},
                config=config,
            )

            index = (root / "docs/contracts/ledger-contracts.md").read_text(
                encoding="utf-8"
            )
            second_page = (
                root / "docs/contracts/ledger-contracts-page-02.md"
            ).read_text(encoding="utf-8")

        self.assertIn("split into 2 reviewable diagrams", index)
        self.assertIn("objects 81-81", second_page.lower())
        self.assertIn("Dto080", second_page)

    def test_renders_deterministic_partitioned_docs_and_diagrams(self) -> None:
        catalog = {
            "schemas": [
                {
                    "name": "ledger",
                    "relations": [
                        {
                            "name": "journal_entries",
                            "kind": "table",
                            "comment": "Posted journals",
                            "columns": [
                                {"name": "journal_entry_id", "data_type": "uuid"}
                            ],
                            "constraints": [
                                {
                                    "name": "pk",
                                    "type": "primary_key",
                                    "columns": ["journal_entry_id"],
                                }
                            ],
                            "indexes": [],
                        }
                    ],
                    "functions": [],
                    "triggers": [],
                    "policies": [],
                }
            ]
        }
        migrations = {
            "migration_sets": [{"id": "ledger", "schema": "ledger"}],
            "files": [],
        }
        contracts = {
            "types": [
                {
                    "full_name": "Meridian.Contracts.Ledger.JournalDto",
                    "contract_sets": ["ledger-contracts"],
                    "classification": "dto",
                    "members": [{"name": "Id", "type": "Guid"}],
                    "references": [],
                }
            ]
        }
        config = {
            "migration_sets": [{"id": "ledger", "schema": "ledger"}],
            "contract_sets": [
                {"id": "ledger-contracts", "schemas": ["ledger"], "diagram": True}
            ],
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = render_snapshot(
                root,
                catalog=catalog,
                migrations=migrations,
                contracts=contracts,
                dependencies={"nodes": [], "edges": []},
                policy_report={"failed": False, "findings": []},
                config=config,
            )
            second = render_snapshot(
                root,
                catalog=catalog,
                migrations=migrations,
                contracts=contracts,
                dependencies={"nodes": [], "edges": []},
                policy_report={"failed": False, "findings": []},
                config=config,
            )

            self.assertGreater(first["artifact_count"], 8)
            self.assertEqual([], second["changed"])
            schema_doc = (root / "docs/modules/ledger.md").read_text(encoding="utf-8")
            self.assertIn("journal_entries", schema_doc)
            contract_doc = (root / "docs/contracts/ledger-contracts.md").read_text(
                encoding="utf-8"
            )
            self.assertIn("not a claim", contract_doc)

    def test_er_diagram_uses_primary_key_marker(self) -> None:
        diagram = render_schema_mermaid(
            {
                "name": "ledger",
                "relations": [
                    {
                        "name": "entries",
                        "columns": [{"name": "id", "data_type": "bigint"}],
                        "constraints": [{"type": "primary_key", "columns": ["id"]}],
                    }
                ],
            }
        )
        self.assertIn("bigint id PK", diagram)

    def test_er_diagram_sanitizes_postgresql_type_syntax(self) -> None:
        diagram = render_schema_mermaid(
            {
                "name": "ledger",
                "relations": [
                    {
                        "name": "entries",
                        "columns": [
                            {"name": "amount", "data_type": "numeric(20, 4)"},
                            {
                                "name": "occurred_at",
                                "data_type": "timestamp with time zone",
                            },
                        ],
                    }
                ],
            }
        )

        self.assertIn("numeric_20__4_ amount", diagram)
        self.assertIn("timestamp_with_time_zone occurred_at", diagram)


if __name__ == "__main__":
    unittest.main()
