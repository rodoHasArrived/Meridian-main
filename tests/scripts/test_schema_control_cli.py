from __future__ import annotations

import io
import json
import os
import subprocess
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import Mock, patch

from tools.schema_control.cli import (
    _configured_output_paths,
    _deleted_migration_findings,
    _prepare_candidate_root,
    _promote,
    _removed_migration_set_findings,
    main,
    parse_args,
)


class SchemaControlCliTests(unittest.TestCase):
    def test_inventory_detects_new_collisions_against_exact_git_base(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migrations = root / "src/Ledger/Migrations"
            migrations.mkdir(parents=True)
            for name in ("closing_entry_posting_kind", "operations_continuity"):
                (migrations / f"V_ledger_008__{name}.sql").write_text("select 1;\n")
            config = {
                "migration_sets": [
                    {"id": "ledger", "directory": "src/Ledger/Migrations", "schema": "ledger"}
                ],
                "migration_reservations": "database/migration-reservations.json",
            }
            register = {"version": 1, "migration_sets": [{
                "id": "ledger", "filename_template": "V_ledger_{ordinal:03d}__{slug}.sql",
                "ordinal_pattern": "^V_ledger_(?P<ordinal>\\d+)__", "reservations": [],
            }]}
            (root / "database/policies").mkdir(parents=True)
            for path, value in (
                ("database/schema-control.json", config),
                ("database/migration-reservations.json", register),
                ("database/policies/schema-control.json", {}),
                ("database/policies/migration-waivers.json", {}),
            ):
                (root / path).write_text(json.dumps(value))
            docs = root / "docs/engineering/blueprints/README.md"
            docs.parent.mkdir(parents=True)
            docs.write_text("<!-- migration-reservations:start -->\n<!-- migration-reservations:end -->\n")
            prefix = ["--root", str(root)]
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(0, main(prefix + ["generate-migration-docs"]))
                for args in (
                    ["init", "-q"], ["add", "."],
                    ["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-qm", "base"],
                ):
                    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)
                baseline = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
                self.assertEqual(0, main(prefix + ["inventory", "--base-ref", baseline]))
                for name in ("V_ledger_008__third.sql", "V_ledger_009__first.sql", "V_ledger_009__second.sql"):
                    (migrations / name).write_text("select 1;\n")
                self.assertEqual(1, main(prefix + ["inventory", "--base-ref", baseline]))
            manifest = json.loads((root / "build/schema-control/migrations.json").read_text())
            collisions = [item for item in manifest["findings"] if item["rule_id"] == "migration-ordinal-duplicate"]
            self.assertEqual(2, len(collisions))
            self.assertTrue(any(item["path"].endswith("V_ledger_008__third.sql") for item in collisions))
            self.assertTrue(any("ordinal 9 collision" in item["message"] for item in collisions))

    def test_new_migration_cli_and_generated_docs_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migrations = root / "src/Ledger/Migrations"
            migrations.mkdir(parents=True)
            (migrations / "V_ledger_041__existing.sql").write_text("select 1;\n")
            config = {
                "migration_sets": [
                    {"id": "ledger", "directory": "src/Ledger/Migrations", "schema": "ledger"}
                ],
                "migration_reservations": "database/migration-reservations.json",
                "detect_unregistered_migration_directories": False,
            }
            register = {
                "version": 1,
                "migration_sets": [{
                    "id": "ledger",
                    "filename_template": "V_ledger_{ordinal:03d}__{slug}.sql",
                    "ordinal_pattern": "^V_ledger_(?P<ordinal>\\d+)__",
                    "reservations": [{
                        "ordinal": 42, "slug": "reserved", "description": "Planned migration",
                        "blueprint": "docs/engineering/blueprints/plan.md",
                    }],
                }],
            }
            (root / "database").mkdir()
            (root / "database/schema-control.json").write_text(json.dumps(config))
            register_path = root / "database/migration-reservations.json"
            register_path.write_text(json.dumps(register))
            readme = root / "docs/engineering/blueprints/README.md"
            readme.parent.mkdir(parents=True)
            readme.write_text(
                "Preserve this introduction.\n<!-- migration-reservations:start -->\n"
                "<!-- migration-reservations:end -->\nPreserve this footer.\n"
            )
            (readme.parent / "plan.md").write_text("Plan\n")
            prefix = ["--root", str(root)]
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(1, main(prefix + ["generate-migration-docs", "--check"]))
                self.assertEqual(0, main(prefix + ["generate-migration-docs"]))
                self.assertEqual(0, main(prefix + ["generate-migration-docs", "--check"]))
                before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
                for ordinal in (41, 42):
                    self.assertEqual(2, main(prefix + [
                        "new-migration", "--migration-set", "ledger", "--name", "new_work",
                        "--ordinal", str(ordinal),
                    ]))
                self.assertEqual(before, {p: p.read_bytes() for p in root.rglob("*") if p.is_file()})
                self.assertEqual(0, main(prefix + [
                    "new-migration", "--migration-set", "ledger", "--name", "new_work",
                ]))
                self.assertEqual(0, main(prefix + ["generate-migration-docs", "--check"]))
            self.assertTrue((migrations / "V_ledger_043__new_work.sql").is_file())
            self.assertEqual(before[register_path], register_path.read_bytes())
            self.assertTrue(readme.read_text().startswith("Preserve this introduction.\n"))
            self.assertTrue(readme.read_text().endswith("Preserve this footer.\n"))

    def test_configured_output_paths_reject_absolute_and_escaping_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "repository"
            root.mkdir()

            for output_path in ("/tmp/schema-control-secret", "../outside-repository"):
                with self.subTest(output_path=output_path):
                    with self.assertRaisesRegex(ValueError, "inside the repository"):
                        _configured_output_paths(
                            root,
                            {
                                "outputs": {
                                    "manifest": output_path,
                                    "docs": "docs/generated/database",
                                }
                            },
                        )

    def test_configured_output_paths_reject_directory_link_escaping_repository(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "repository"
            root.mkdir()
            outside = Path(directory) / "outside"
            outside.mkdir()
            linked_output = root / "linked-output"
            if os.name == "nt":
                # A real directory junction exercises path resolution without the
                # Windows privilege required to create symbolic links.
                subprocess.run(
                    ["cmd", "/c", "mklink", "/J", str(linked_output), str(outside)],
                    check=True,
                    capture_output=True,
                    text=True,
                )
            else:
                linked_output.symlink_to(outside, target_is_directory=True)
            self.assertEqual(outside.resolve(), linked_output.resolve())

            with self.assertRaisesRegex(ValueError, "inside the repository"):
                _configured_output_paths(
                    root,
                    {
                        "outputs": {
                            "manifest": "linked-output",
                            "docs": "docs/generated/database",
                        }
                    },
                )

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

            with self.assertRaisesRegex(ValueError, "inside the repository"):
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
