from __future__ import annotations

import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from tools.schema_control.migrations import build_migration_inventory
from tools.schema_control.reservations import (
    generate_migration_docs,
    load_reservation_register,
    reservation_findings,
    scaffold_migration,
)


class SchemaControlReservationTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.config = {
            "migration_reservations": "database/migration-reservations.json",
            "migration_sets": [
                {
                    "id": "ledger",
                    "directory": "src/Ledger/Migrations",
                    "schema": "ledger",
                    "track_ordinals": False,
                }
            ],
        }
        self.register = {
            "version": 1,
            "migration_sets": [
                {
                    "id": "ledger",
                    "filename_template": "V_ledger_{ordinal:03d}__{slug}.sql",
                    "ordinal_pattern": r"^V_ledger_(?P<ordinal>\d+)__",
                    "reservations": [self._reservation(42, "equalization_policy")],
                }
            ],
        }
        self.register_path = self.root / self.config["migration_reservations"]
        self.docs_path = self._write(
            "docs/engineering/blueprints/README.md",
            "# Blueprint index\n\n"
            "Before the generated section.\n\n"
            "<!-- migration-reservations:start -->\n"
            "stale generated content\n"
            "<!-- migration-reservations:end -->\n\n"
            "After the generated section.\n",
        )
        self._write(self._reservation(42, "equalization_policy")["blueprint"], "# Plan\n")
        (self.root / "src/Ledger/Migrations").mkdir(parents=True)
        self._save_register()

    @staticmethod
    def _reservation(ordinal: int, slug: str) -> dict:
        return {
            "ordinal": ordinal,
            "slug": slug,
            "description": slug.replace("_", " ").capitalize(),
            "blueprint": "docs/engineering/blueprints/accounting/equalization-and-series-accounting.md",
        }

    def _write(self, relative_path: str, content: str) -> Path:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def _migration(self, ordinal: int, slug: str, *, content: str = "select 1;\n") -> Path:
        return self._write(
            f"src/Ledger/Migrations/V_ledger_{ordinal:03d}__{slug}.sql", content
        )

    def _save_register(self) -> None:
        self._write(self.config["migration_reservations"], json.dumps(self.register) + "\n")

    def _snapshot(self) -> dict[str, bytes]:
        return {
            path.relative_to(self.root).as_posix(): path.read_bytes()
            for path in self.root.rglob("*")
            if path.is_file()
        }

    def _findings(self, baseline: dict[str, bytes] | None = None):
        inventory = build_migration_inventory(self.root, self.config)
        self.assertFalse(inventory.has_errors, inventory.findings)
        register = load_reservation_register(self.root, self.config)
        return reservation_findings(
            inventory, register, base_reader=None if baseline is None else baseline.get
        )

    def test_automatic_allocation_skips_consecutive_reserved_numbers(self) -> None:
        existing = self._migration(41, "current", content="-- applied migration\nselect 1;\n")
        applied_bytes = existing.read_bytes()
        self.register["migration_sets"][0]["reservations"].append(
            self._reservation(43, "subscription_lots")
        )
        self._save_register()
        register_bytes = self.register_path.read_bytes()

        created = scaffold_migration(self.root, self.config, "ledger", "cash_reconciliation")

        self.assertEqual(
            self.root / "src/Ledger/Migrations/V_ledger_044__cash_reconciliation.sql",
            created,
        )
        self.assertTrue(created.read_text(encoding="utf-8").strip())
        self.assertEqual(applied_bytes, existing.read_bytes())
        self.assertEqual(register_bytes, self.register_path.read_bytes())
        self.assertTrue(generate_migration_docs(self.root, self.config, check=True))

    def test_concurrent_scaffolds_allocate_distinct_numbers_and_keep_docs_current(self) -> None:
        self._migration(41, "current")
        register_bytes = self.register_path.read_bytes()
        script = (
            "import json, sys\n"
            "from pathlib import Path\n"
            "from tools.schema_control.reservations import scaffold_migration\n"
            "print('ready', flush=True)\n"
            "sys.stdin.readline()\n"
            "created = scaffold_migration(Path(sys.argv[1]), json.loads(sys.argv[2]), "
            "'ledger', sys.argv[3])\n"
            "print(created.name, flush=True)\n"
        )
        processes = []
        slugs = ("parallel_one", "parallel_two", "parallel_three", "parallel_four")
        for slug in slugs:
            process = subprocess.Popen(
                [sys.executable, "-c", script, str(self.root), json.dumps(self.config), slug],
                cwd=Path(__file__).resolve().parents[2],
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            processes.append(process)
            self.addCleanup(self._finish_process, process)

        # Release all contenders only after their imports are complete so the
        # test exercises simultaneous allocation rather than startup timing.
        for process in processes:
            self.assertEqual("ready\n", process.stdout.readline())
        for process in processes:
            process.stdin.write("start\n")
            process.stdin.flush()

        filenames = []
        for process in processes:
            stdout, stderr = process.communicate(timeout=15)
            self.assertEqual(0, process.returncode, stderr)
            filenames.append(stdout.strip())

        self.assertEqual({43, 44, 45, 46}, {int(name.split("__")[0].rsplit("_", 1)[1]) for name in filenames})
        self.assertEqual(4, len(set(filenames)))
        self.assertEqual(register_bytes, self.register_path.read_bytes())
        self.assertTrue(generate_migration_docs(self.root, self.config, check=True))
        self.assertRegex(
            self.docs_path.read_text(encoding="utf-8"),
            r"[Hh]ighest[^\n]*V_ledger_046__parallel_",
        )

    @staticmethod
    def _finish_process(process: subprocess.Popen) -> None:
        if process.poll() is None:
            process.kill()
        process.communicate()

    def test_higher_reservation_does_not_skip_available_numbers_below_it(self) -> None:
        self._migration(39, "current")

        created = scaffold_migration(self.root, self.config, "ledger", "new_feature")

        self.assertEqual("V_ledger_040__new_feature.sql", created.name)

    def test_explicit_free_ordinal_uses_requested_filename_and_preserves_applied_sql(self) -> None:
        self._migration(8, "closing_entry_posting_kind", content="-- applied closing\n")
        self._migration(8, "operations_continuity", content="-- applied continuity\n")
        self._migration(41, "current")
        before = self._snapshot()

        created = scaffold_migration(self.root, self.config, "ledger", "new_feature", ordinal=48)

        self.assertEqual("V_ledger_048__new_feature.sql", created.name)
        for relative_path, content in before.items():
            if relative_path.endswith(".sql"):
                self.assertEqual(content, (self.root / relative_path).read_bytes())

    def test_occupied_number_rejected_even_with_a_different_slug_without_writes(self) -> None:
        self._migration(41, "existing")
        before = self._snapshot()

        for slug in ("existing", "different_feature"):
            with self.subTest(slug=slug):
                with self.assertRaisesRegex(ValueError, r"(?i)(occupied|exist|use|taken)"):
                    scaffold_migration(self.root, self.config, "ledger", slug, ordinal=41)
                self.assertEqual(before, self._snapshot())

    def test_reserved_number_rejected_even_for_its_owner_without_writes(self) -> None:
        self._migration(41, "current")
        before = self._snapshot()

        for slug in ("equalization_policy", "different_feature"):
            with self.subTest(slug=slug):
                with self.assertRaisesRegex(ValueError, r"(?i)reserv"):
                    scaffold_migration(self.root, self.config, "ledger", slug, ordinal=42)
                self.assertEqual(before, self._snapshot())

    def test_invalid_ordinal_slug_and_unknown_set_rejected_without_writes(self) -> None:
        self._migration(41, "current")
        before = self._snapshot()
        cases = [
            ("ledger", "feature", 0),
            ("ledger", "feature", -1),
            ("ledger", "feature", True),
            ("ledger", "feature", 1.5),
            ("ledger", "feature", "48"),
            ("ledger", "", 48),
            ("ledger", "../escape", 48),
            ("ledger", "has spaces", 48),
            ("ledger", "has-hyphens", 48),
            ("ledger", "UpperCase", 48),
            ("unknown", "feature", 48),
        ]
        for migration_set, name, ordinal in cases:
            with self.subTest(migration_set=migration_set, name=name, ordinal=ordinal):
                with self.assertRaises(ValueError):
                    scaffold_migration(
                        self.root, self.config, migration_set, name, ordinal=ordinal
                    )
                self.assertEqual(before, self._snapshot())

    def test_duplicate_reservation_ordinals_and_set_ids_fail_closed(self) -> None:
        original = copy.deepcopy(self.register)
        duplicate_ordinal = copy.deepcopy(original)
        duplicate_ordinal["migration_sets"][0]["reservations"].append(
            self._reservation(42, "another_claim")
        )
        duplicate_set = copy.deepcopy(original)
        duplicate_set["migration_sets"].append(copy.deepcopy(duplicate_set["migration_sets"][0]))

        for register in (duplicate_ordinal, duplicate_set):
            with self.subTest(register=register):
                self.register = register
                self._save_register()
                before = self._snapshot()
                with self.assertRaises(ValueError):
                    load_reservation_register(self.root, self.config)
                with self.assertRaises(ValueError):
                    scaffold_migration(self.root, self.config, "ledger", "new_feature")
                self.assertEqual(before, self._snapshot())

    def test_malformed_register_ordinals_and_missing_register_fail_closed(self) -> None:
        original = copy.deepcopy(self.register)
        for ordinal in (0, -1, True, "42", 42.5):
            with self.subTest(ordinal=ordinal):
                self.register = copy.deepcopy(original)
                self.register["migration_sets"][0]["reservations"][0]["ordinal"] = ordinal
                self._save_register()
                with self.assertRaises(ValueError):
                    load_reservation_register(self.root, self.config)

        self.register_path.unlink()
        before = self._snapshot()
        with self.assertRaises(ValueError):
            scaffold_migration(self.root, self.config, "ledger", "new_feature")
        self.assertEqual(before, self._snapshot())

    def test_occupied_reservation_is_reported_and_prevents_scaffolding(self) -> None:
        self._migration(42, "some_already_applied_feature")

        findings = self._findings()

        self.assertTrue(any(item.severity == "error" for item in findings), findings)
        self.assertTrue(any("42" in item.message for item in findings), findings)
        before = self._snapshot()
        with self.assertRaises(ValueError):
            scaffold_migration(self.root, self.config, "ledger", "new_feature")
        self.assertEqual(before, self._snapshot())

    def test_generated_docs_use_actual_files_and_reservations_deterministically(self) -> None:
        self._migration(8, "operations_continuity")
        self._migration(8, "closing_entry_posting_kind")
        self._migration(41, "current")
        self.register["migration_sets"][0]["reservations"].insert(
            0, self._reservation(44, "fund_series")
        )
        self._save_register()
        before = self._snapshot()

        self.assertFalse(generate_migration_docs(self.root, self.config, check=True))
        self.assertEqual(before, self._snapshot())
        self.assertTrue(generate_migration_docs(self.root, self.config))
        rendered = self.docs_path.read_text(encoding="utf-8")

        for name in (
            "V_ledger_008__closing_entry_posting_kind.sql",
            "V_ledger_008__operations_continuity.sql",
            "V_ledger_041__current.sql",
            "equalization_policy",
            "fund_series",
            "042",
            "044",
            "equalization-and-series-accounting.md",
            "Before the generated section.",
            "After the generated section.",
        ):
            self.assertIn(name, rendered)
        self.assertLess(rendered.index("equalization_policy"), rendered.index("fund_series"))
        self.assertRegex(rendered, r"[Hh]ighest[^\n]*V_ledger_041__current\.sql")
        self.assertTrue(generate_migration_docs(self.root, self.config, check=True))
        generated_bytes = self.docs_path.read_bytes()
        self.assertFalse(generate_migration_docs(self.root, self.config))
        self.assertEqual(generated_bytes, self.docs_path.read_bytes())

    def test_document_publish_failure_rolls_back_scaffold_and_temporary_files(self) -> None:
        self._migration(41, "current")
        before = self._snapshot()

        with patch(
            "tools.schema_control.reservations.os.replace",
            side_effect=OSError("synthetic documentation publish failure"),
        ):
            with self.assertRaisesRegex(OSError, "synthetic documentation publish failure"):
                scaffold_migration(self.root, self.config, "ledger", "new_feature")

        self.assertEqual(before, self._snapshot())

    def test_missing_or_duplicate_docs_markers_fail_before_scaffold_writes(self) -> None:
        self._migration(41, "current")
        for content in (
            "# No generated markers\n",
            "<!-- migration-reservations:start -->\n"
            "<!-- migration-reservations:end -->\n"
            "<!-- migration-reservations:start -->\n"
            "<!-- migration-reservations:end -->\n",
        ):
            with self.subTest(content=content):
                self.docs_path.write_text(content, encoding="utf-8")
                before = self._snapshot()
                with self.assertRaises(ValueError):
                    scaffold_migration(self.root, self.config, "ledger", "new_feature")
                self.assertEqual(before, self._snapshot())

    def test_baseline_preserves_historical_008_pair_without_renumbering(self) -> None:
        self._migration(8, "closing_entry_posting_kind", content="-- historical closing\n")
        self._migration(8, "operations_continuity", content="-- historical continuity\n")
        baseline = self._snapshot()

        self.assertEqual([], self._findings(baseline))
        self.assertEqual(baseline, self._snapshot())

    def test_third_008_introduced_since_baseline_is_rejected(self) -> None:
        self._migration(8, "closing_entry_posting_kind")
        self._migration(8, "operations_continuity")
        baseline = self._snapshot()
        introduced = self._migration(8, "new_collision")

        findings = self._findings(baseline)

        self.assertTrue(any(item.severity == "error" for item in findings), findings)
        self.assertTrue(
            any(item.path == introduced.relative_to(self.root).as_posix() for item in findings),
            findings,
        )

    def test_new_duplicate_group_wholly_absent_from_baseline_is_rejected(self) -> None:
        self._migration(48, "first_new_feature")
        self._migration(48, "second_new_feature")

        findings = self._findings({})

        self.assertTrue(any(item.severity == "error" for item in findings), findings)
        self.assertTrue(any("48" in item.message for item in findings), findings)

    def test_duplicate_check_without_baseline_does_not_reject_applied_history(self) -> None:
        self._migration(8, "closing_entry_posting_kind")
        self._migration(8, "operations_continuity")

        self.assertEqual([], self._findings())

    def test_same_ordinal_in_different_migration_sets_is_allowed(self) -> None:
        self._migration(8, "ledger_feature")
        self.config["migration_sets"].append(
            {
                "id": "reporting",
                "directory": "src/Reporting/Migrations",
                "schema": "reporting",
                "track_ordinals": False,
            }
        )
        self.register["migration_sets"].append(
            {
                "id": "reporting",
                "filename_template": "{ordinal:03d}_{slug}.sql",
                "ordinal_pattern": r"^(?P<ordinal>\d+)_",
                "reservations": [],
            }
        )
        self._write("src/Reporting/Migrations/008_reporting_feature.sql", "select 2;\n")
        self._save_register()

        self.assertEqual([], self._findings({}))


if __name__ == "__main__":
    unittest.main()
