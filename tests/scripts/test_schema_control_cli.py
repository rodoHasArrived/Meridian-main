from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from tools.schema_control.cli import (
    _deleted_migration_findings,
    _prepare_candidate_root,
    _promote,
    _removed_migration_set_findings,
    main,
    parse_args,
)


class SchemaControlCliTests(unittest.TestCase):
    def test_promote_validates_all_sources_before_replacing_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "repository"
            candidate = root / "build/schema-control/candidate"
            (candidate / "manifest").mkdir(parents=True)
            (candidate / "manifest/new.json").write_text("{}", encoding="utf-8")
            tracked_manifest = root / "database/manifest"
            tracked_manifest.mkdir(parents=True)
            (tracked_manifest / "existing.json").write_text("{}", encoding="utf-8")
            config = {
                "outputs": {
                    "manifest": "database/manifest",
                    "docs": "docs/generated/database",
                }
            }

            with self.assertRaisesRegex(ValueError, "Candidate artifact directory"):
                _promote(root, config, candidate)

            self.assertTrue((tracked_manifest / "existing.json").exists())
            self.assertFalse((tracked_manifest / "new.json").exists())

    def test_promote_rejects_repository_root_destination(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "repository"
            candidate = root / "build/schema-control/candidate"
            (candidate / "manifest").mkdir(parents=True)
            (candidate / "docs").mkdir()
            sentinel = root / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "outside the repository"):
                _promote(
                    root,
                    {"outputs": {"manifest": ".", "docs": "docs/generated/database"}},
                    candidate,
                )

            self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))

    def test_removed_and_renamed_baseline_migrations_fail_closed(self) -> None:
        config = {
            "migration_sets": [
                {
                    "id": "ledger",
                    "directory": "src/Ledger/Migrations",
                    "schema": "ledger",
                }
            ]
        }
        git_result = Mock(
            returncode=0,
            stdout=(
                "R100\tsrc/Ledger/Migrations/001_old.sql\t"
                "src/Ledger/Migrations/002_new.sql\n"
                "D\tsrc/Removed/Migrations/001_gone.sql\n"
            ),
            stderr="",
        )
        with patch("tools.schema_control.cli.subprocess.run", return_value=git_result):
            findings = _deleted_migration_findings(
                Path("."), "origin/main", config, None
            )

        self.assertEqual(
            [
                "migration-immutable-file-renamed",
                "migration-immutable-file-removed",
            ],
            [item.rule_id for item in findings],
        )

    def test_removed_baseline_migration_set_fails_closed(self) -> None:
        baseline = {
            "migration_sets": [
                {
                    "id": "retired",
                    "directory": "src/Retired/Migrations",
                    "schema": "retired",
                }
            ]
        }

        findings = _removed_migration_set_findings(baseline, {"migration_sets": []})

        self.assertEqual(1, len(findings))
        self.assertEqual("migration-set-registry-removed", findings[0].rule_id)

    def test_candidate_preparation_removes_only_owned_stale_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "repository"
            candidate = root / "build/schema-control/candidate"
            (candidate / "manifest").mkdir(parents=True)
            (candidate / "manifest/stale.json").write_text("{}", encoding="utf-8")
            (candidate / "docs").mkdir()
            (candidate / "docs/stale.md").write_text("stale", encoding="utf-8")
            (candidate / "unowned.txt").write_text("keep", encoding="utf-8")

            _prepare_candidate_root(root, candidate)

            self.assertFalse((candidate / "manifest/stale.json").exists())
            self.assertFalse((candidate / "docs/stale.md").exists())
            self.assertTrue((candidate / "unowned.txt").exists())

            with self.assertRaisesRegex(ValueError, "repository root"):
                _prepare_candidate_root(root, root)

    def test_parser_exposes_inventory_snapshot_verify_and_promote(self) -> None:
        for command in ("inventory", "snapshot", "verify", "promote"):
            argv = [command]
            if command in {"snapshot", "verify"}:
                argv.extend(["--database-url", "postgresql://example"])
            self.assertEqual(command, parse_args(argv).command)

    def test_inventory_returns_one_for_duplicate_tracked_ordinal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migrations = root / "db/Migrations"
            migrations.mkdir(parents=True)
            (migrations / "001_a.sql").write_text("select 1;", encoding="utf-8")
            (migrations / "001_b.sql").write_text("select 2;", encoding="utf-8")
            (root / "database/policies").mkdir(parents=True)
            config = {
                "version": 1,
                "migration_sets": [
                    {
                        "id": "db",
                        "directory": "db/Migrations",
                        "schema": "db",
                        "track_ordinals": True,
                        "ordinal_pattern": "^(\\d+)_",
                    }
                ],
                "migration_search_roots": ["db"],
            }
            (root / "database/schema-control.json").write_text(
                json.dumps(config), encoding="utf-8"
            )
            (root / "database/policies/schema-control.json").write_text(
                "{}", encoding="utf-8"
            )
            (root / "database/policies/migration-waivers.json").write_text(
                "{}", encoding="utf-8"
            )

            result = main(["--root", str(root), "inventory"])

            self.assertEqual(1, result)
            report = json.loads(
                (root / "build/schema-control/migrations.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual(1, report["summary"]["errors"])

    def test_inventory_fails_closed_for_overdue_waiver(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migrations = root / "db/Migrations"
            migrations.mkdir(parents=True)
            (migrations / "001_a.sql").write_text("select 1;", encoding="utf-8")
            (root / "database/policies").mkdir(parents=True)
            config = {
                "migration_sets": [
                    {
                        "id": "db",
                        "directory": "db/Migrations",
                        "schema": "db",
                    }
                ],
                "migration_search_roots": ["db"],
            }
            waivers = {
                "removed_migrations": [
                    {
                        "path": "db/Migrations/retired.sql",
                        "reason": "Reviewed test exception.",
                        "review_after": "2000-01-01",
                    }
                ]
            }
            (root / "database/schema-control.json").write_text(
                json.dumps(config), encoding="utf-8"
            )
            (root / "database/policies/schema-control.json").write_text(
                "{}", encoding="utf-8"
            )
            (root / "database/policies/migration-waivers.json").write_text(
                json.dumps(waivers), encoding="utf-8"
            )

            result = main(["--root", str(root), "inventory"])

            self.assertEqual(1, result)
            report = json.loads(
                (root / "build/schema-control/migrations.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual(
                ["migration-waiver-review-overdue"],
                [item["rule_id"] for item in report["findings"]],
            )

    def test_snapshot_orchestration_uses_database_and_contract_layers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "database/policies").mkdir(parents=True)
            (root / "db/Migrations").mkdir(parents=True)
            (root / "db/Migrations/001_a.sql").write_text("select 1;", encoding="utf-8")
            config = {
                "migration_sets": [
                    {"id": "db", "directory": "db/Migrations", "schema": "db"}
                ],
                "migration_search_roots": ["db"],
                "contract_sets": [],
            }
            (root / "database/schema-control.json").write_text(
                json.dumps(config), encoding="utf-8"
            )
            (root / "database/policies/schema-control.json").write_text(
                "{}", encoding="utf-8"
            )
            (root / "database/policies/migration-waivers.json").write_text(
                "{}", encoding="utf-8"
            )
            with (
                patch("tools.schema_control.cli.apply_migrations") as apply,
                patch(
                    "tools.schema_control.catalog.extract_catalog",
                    return_value={"schemas": []},
                ),
                patch(
                    "tools.schema_control.contracts.build_contract_manifest",
                    return_value={"types": []},
                ),
                patch(
                    "tools.schema_control.policies.evaluate_policies",
                    return_value={"failed": False, "findings": []},
                ),
            ):
                apply.return_value.applied = ("db/Migrations/001_a.sql",)
                apply.return_value.skipped = ()
                result = main(
                    [
                        "--root",
                        str(root),
                        "snapshot",
                        "--database-url",
                        "postgresql://example",
                    ]
                )

            self.assertEqual(0, result)
            candidate = root / "build/schema-control/candidate"
            self.assertTrue((candidate / "manifest/catalog.json").exists())
            migration_manifest = json.loads(
                (candidate / "manifest/migrations.json").read_text(encoding="utf-8")
            )
            self.assertNotIn("application", migration_manifest)
            self.assertTrue((candidate / "reports/migration-application.json").exists())


if __name__ == "__main__":
    unittest.main()
