from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/generate-contract-manifest.py"
SPEC = importlib.util.spec_from_file_location("generate_contract_manifest", SCRIPT)
generation = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generation)


class ContractGenerationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write_json("database/schema-control.json", {
            "contract_sets": [{"id": "ledger-contracts", "directories": ["src/Contracts"],
                               "namespace_prefixes": ["Meridian.Contracts"],
                               "schemas": ["ledger"], "diagram": True}],
            "migration_sets": [],
        })
        self.write_json("database/manifest/catalog.json", {"schemas": [{"name": "ledger", "relations": []}]})
        self.write_json("database/manifest/migrations.json", {"migration_sets": [], "files": []})
        self.write_json("database/manifest/policies.json", {"failed": False, "findings": []})
        self.write("docs/source/generated/source-hash-manifest.json", "reviewed baseline\n")
        self.write_contracts(2)

    def write(self, relative: str, value: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def write_json(self, relative: str, value: dict) -> None:
        self.write(relative, json.dumps(value) + "\n")

    def write_contracts(self, count: int) -> None:
        self.write("src/Contracts/Items.cs", "namespace Meridian.Contracts;\n" + "\n".join(
            f"public record Item{number:03d}(decimal Amount);" for number in range(count)))

    def snapshot(self) -> dict[str, bytes]:
        return {path.relative_to(self.root).as_posix(): path.read_bytes()
                for path in self.root.rglob("*") if path.is_file()}

    def test_refresh_is_deterministic_and_preserves_reviewed_physical_inputs(self) -> None:
        before = self.snapshot()
        changed = generation.generate(self.root)
        self.assertIn("database/manifest/contracts.json", changed)
        self.assertIn("database/manifest/dependencies.json", changed)
        self.assertIn("docs/generated/database/contracts/ledger-contracts.md", changed)
        after = self.snapshot()
        for relative, content in before.items():
            self.assertEqual(content, after[relative], relative)
        self.assertEqual([], generation.generate(self.root))
        self.assertEqual(after, self.snapshot())

    def test_check_detects_source_changes_without_modifying_any_files(self) -> None:
        generation.generate(self.root)
        self.write_contracts(3)
        before = self.snapshot()
        result = subprocess.run([sys.executable, str(SCRIPT), "--repo", str(self.root), "--check"],
                                capture_output=True, text=True)
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertNotIn("Stale: database/manifest/contracts.json", result.stdout)
        self.assertIn("Stale: docs/generated/database/contracts/ledger-contracts.md", result.stdout)
        self.assertEqual(before, self.snapshot())
        generation.generate(self.root)
        self.assertEqual([], generation.generate(self.root, check=True))

    def test_missing_or_stale_machine_inventory_does_not_fail_reviewed_freshness(self) -> None:
        generation.generate(self.root)
        raw = self.root / "database/manifest/contracts.json"
        raw.unlink()
        self.assertEqual([], generation.generate(self.root, check=True))
        raw.write_text("outdated machine inventory\n")
        self.assertEqual([], generation.generate(self.root, check=True))
        # A source comment changes mechanical source provenance, not reviewed
        # contract structure or diagrams. Default generation still emits it.
        path = self.root / "src/Contracts/Items.cs"
        path.write_text(path.read_text() + "\n// implementation note\n")
        self.assertEqual([], generation.generate(self.root, check=True))
        self.assertEqual(["database/manifest/contracts.json"], generation.generate(self.root))

    def test_shrinking_contract_set_removes_obsolete_diagram_pages(self) -> None:
        self.write_contracts(81)
        generation.generate(self.root)
        page = "docs/generated/database/diagrams/contracts-ledger-contracts-page-02.mmd"
        self.assertTrue((self.root / page).is_file())
        self.write_contracts(2)
        changed = generation.generate(self.root)
        self.assertIn(page, changed)
        self.assertFalse((self.root / page).exists())
        self.assertTrue((self.root / "docs/generated/database/diagrams/contracts-ledger-contracts.mmd").is_file())

    def test_symlink_output_is_rejected_without_writing_outside_checkout(self) -> None:
        generation.generate(self.root)
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as outside:
            target = Path(outside) / "reviewed.txt"
            target.write_text("unchanged")
            path = self.root / "docs/generated/database/data-object-catalog.md"
            path.unlink()
            try:
                path.symlink_to(target)
            except OSError:
                self.skipTest("Symlinks unavailable")
            with self.assertRaisesRegex(ValueError, "symlinks"):
                generation.generate(self.root)
            self.assertEqual("unchanged", target.read_text())

    def test_changed_output_configuration_requires_policy_review(self) -> None:
        config = json.loads((self.root / "database/schema-control.json").read_text())
        config["outputs"] = {"docs": "docs/handwritten"}
        self.write_json("database/schema-control.json", config)
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "policy review"):
            generation.generate(self.root)
        self.assertEqual(before, self.snapshot())


if __name__ == "__main__":
    unittest.main()
