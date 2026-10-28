"""Source documentation hashes must survive checkout platform changes."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


DOCS_SCRIPT_DIR = Path(__file__).resolve().parents[2] / "build" / "scripts" / "docs"
sys.path.insert(0, str(DOCS_SCRIPT_DIR))
spec = importlib.util.spec_from_file_location(
    "validate_doc_hashes_under_test", DOCS_SCRIPT_DIR / "validate-doc-hashes.py"
)
assert spec is not None and spec.loader is not None
doc_hashes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(doc_hashes)


class SourceDocHashPortabilityTests(unittest.TestCase):
    def test_source_order_is_ordinal_and_independent_of_checkout_root(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src" / "Sample"
            source.mkdir(parents=True)
            for filename in ("apple.cs", "Zebra.cs"):
                (source / filename).write_bytes(b"class Example {}\n")

            content_hash = hashlib.sha256(b"class Example {}\n").hexdigest()
            expected = hashlib.sha256(
                f"src/Sample/Zebra.cs:{content_hash}\nsrc/Sample/apple.cs:{content_hash}".encode()
            ).hexdigest()
            self.assertEqual(expected, doc_hashes.tree_hash(root, source))

    def test_source_text_hash_survives_lf_to_crlf_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            path = source / "Example.csproj"
            path.write_bytes(b"<Project>\n</Project>\n")
            before = doc_hashes.tree_hash(root, source)
            path.write_bytes(b"<Project>\r\n</Project>\r\n")
            self.assertEqual(before, doc_hashes.tree_hash(root, source))

            path.write_bytes(b"<Project Changed='true'>\r\n</Project>\r\n")
            self.assertNotEqual(before, doc_hashes.tree_hash(root, source))

    def test_readme_normalizes_newlines_but_preserves_documentation_changes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            registry = root / "docs/source/data/source-modules.yml"
            registry.parent.mkdir(parents=True)
            registry.write_text(
                "modules:\n  - id: SRC-SAMPLE\n    path: src/Sample\n"
                "    readme: src/Sample/README.md\n", encoding="utf-8"
            )
            source = root / "src/Sample"
            source.mkdir(parents=True)
            readme = source / "README.md"
            readme.write_bytes(b"# Sample\n\nOwned workflow.\n")
            before = doc_hashes.build_manifest(root)["modules"][0]["readme_hash"]
            readme.write_bytes(b"# Sample\r\n\r\nOwned workflow.\r\n")
            self.assertEqual(before, doc_hashes.build_manifest(root)["modules"][0]["readme_hash"])

            readme.write_bytes(b"# Sample\r\n\r\nChanged workflow.\r\n")
            self.assertNotEqual(before, doc_hashes.build_manifest(root)["modules"][0]["readme_hash"])

    def test_non_utf8_source_bytes_are_not_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            payload = b"\xff\r\n"
            (source / "legacy.json").write_bytes(payload)
            content_hash = hashlib.sha256(payload).hexdigest()
            expected = hashlib.sha256(f"src/legacy.json:{content_hash}".encode()).hexdigest()
            self.assertEqual(expected, doc_hashes.tree_hash(root, source))

    def test_build_and_dependency_outputs_stay_excluded(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            (source / "Example.cs").write_bytes(b"class Example {}\n")
            before = doc_hashes.tree_hash(root, source)
            for name in ("bin", "obj", "node_modules", "wwwroot"):
                ignored = source / name
                ignored.mkdir()
                (ignored / "generated.cs").write_bytes(b"generated output\n")
            self.assertEqual(before, doc_hashes.tree_hash(root, source))


class ReviewedReadmeFingerprintTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / "src/Sample"
        self.source.mkdir(parents=True)
        self.readme = self.source / "README.md"
        self.code = self.source / "Sample.cs"
        self.code.write_text("class Sample {}\n")
        registry = self.root / "docs/source/data/source-modules.yml"
        registry.parent.mkdir(parents=True)
        registry.write_text(json.dumps({"modules": [{
            "id": "SRC-SAMPLE", "path": "src/Sample", "readme": "src/Sample/README.md",
            "roadmap_items": [],
        }]}))
        (registry.parent / "source-todos.yml").write_text('{"todos":[]}')
        roadmap = self.root / "docs/roadmap/data/roadmap-items.yml"
        roadmap.parent.mkdir(parents=True)
        roadmap.write_text('{"items":[]}')
        self.text = (
            "# Sample\n\nHandwritten workflow.\n\n"
            "<!-- source-roadmap-traceability:begin module=SRC-SAMPLE -->\n"
            "- No roadmap items registered.\n"
            "<!-- source-roadmap-traceability:end -->\n\n"
            "Handwritten checklist instructions.\n\n"
            "<!-- source-todos:begin module=SRC-SAMPLE -->\n"
            "- No registry-backed TODOs are open for this module.\n"
            "<!-- source-todos:end -->\n"
        )
        self.readme.write_text(self.text)
        self.manifest_path = self.root / doc_hashes.HASH_MANIFEST
        self.manifest_path.parent.mkdir(parents=True)
        self.legacy = doc_hashes.build_manifest(self.root)
        self.manifest_path.write_text(json.dumps(self.legacy))

    def migrate(self) -> dict:
        changed, count = doc_hashes.migrate_reviewed_readmes(self.root)
        self.assertTrue(changed)
        self.assertEqual(count, 1)
        return json.loads(self.manifest_path.read_text())

    def test_v1_default_preserves_full_readme_fingerprint(self) -> None:
        self.readme.write_text(self.text.replace("- No roadmap items registered.", "New generated text."))
        actual = doc_hashes.build_manifest(self.root)
        self.assertEqual(actual["schema"]["version"], "1.0.0")
        self.assertNotEqual(actual["modules"][0]["readme_hash"], self.legacy["modules"][0]["readme_hash"])
        self.assertTrue(doc_hashes.validate_source_hash_manifest(self.root, actual))

    def test_migration_preserves_accepted_source_even_when_source_is_stale(self) -> None:
        self.code.write_text("class Sample { public void Unreviewed() {} }\n")
        migrated = self.migrate()
        self.assertEqual(migrated["schema"]["version"], "2.0.0")
        self.assertEqual(migrated["modules"][0]["source_hash"], self.legacy["modules"][0]["source_hash"])
        findings = doc_hashes.validate_source_hash_manifest(self.root, doc_hashes.build_manifest(self.root))
        self.assertTrue(any("source hash drift" in finding.message for finding in findings))

    def test_migration_preview_preserves_manifest_bytes(self) -> None:
        before = self.manifest_path.read_bytes()
        self.assertEqual(doc_hashes.migrate_reviewed_readmes(self.root, apply=False), (False, 1))
        self.assertEqual(before, self.manifest_path.read_bytes())

    def test_unreviewed_readme_migration_refuses_without_writing(self) -> None:
        before = self.manifest_path.read_bytes()
        self.readme.write_text(self.text.replace("Handwritten workflow.", "Unreviewed workflow."))
        with self.assertRaisesRegex(ValueError, "do not match the reviewed baseline"):
            doc_hashes.migrate_reviewed_readmes(self.root)
        self.assertEqual(before, self.manifest_path.read_bytes())

    def test_generated_only_changes_keep_review_hash_but_fail_freshness(self) -> None:
        migrated = self.migrate()
        self.assertEqual(doc_hashes.validate_readme_generated_blocks(self.root), [])
        self.readme.write_text(self.text.replace("- No roadmap items registered.", "Stale generated text."))
        actual = doc_hashes.build_manifest(self.root)
        self.assertEqual(migrated["modules"][0]["readme_hash"], actual["modules"][0]["readme_hash"])
        self.assertEqual(doc_hashes.validate_source_hash_manifest(self.root, actual), [])
        self.assertTrue(any("generated block is stale" in finding.message
                            for finding in doc_hashes.validate_readme_generated_blocks(self.root)))

    def test_handwritten_edit_changes_v2_fingerprint(self) -> None:
        migrated = self.migrate()
        self.readme.write_text(self.text.replace("Handwritten workflow.", "Changed workflow."))
        actual = doc_hashes.build_manifest(self.root)
        self.assertNotEqual(migrated["modules"][0]["readme_hash"], actual["modules"][0]["readme_hash"])
        self.assertTrue(doc_hashes.validate_source_hash_manifest(self.root, actual))

    def test_v2_newline_normalization_is_portable(self) -> None:
        migrated = self.migrate()
        self.readme.write_bytes(self.text.replace("\n", "\r\n").encode())
        self.assertEqual(migrated, doc_hashes.build_manifest(self.root))
        self.assertEqual(doc_hashes.validate_readme_generated_blocks(self.root), [])

    def test_malformed_missing_duplicate_wrong_module_and_nested_markers_fail(self) -> None:
        self.migrate()
        begin = "<!-- source-roadmap-traceability:begin module=SRC-SAMPLE -->"
        end = "<!-- source-roadmap-traceability:end -->"
        variants = [
            self.text.replace(end, ""),
            self.text + begin + "\n",
            self.text.replace("module=SRC-SAMPLE", "module=SRC-OTHER", 1),
            self.text.replace(begin, begin + " stray prose"),
            self.text.replace(end, "").replace("<!-- source-todos:end -->", end + "\n<!-- source-todos:end -->"),
            self.text.replace(begin, "<!-- source-roadmap-traceability:begin module=SRC-SAMPLE-->"),
        ]
        for text in variants:
            with self.subTest(text=text):
                self.readme.write_text(text)
                with self.assertRaises(ValueError):
                    doc_hashes.build_manifest(self.root)

    def test_schema_downgrade_or_upgrade_is_not_a_narrow_refresh(self) -> None:
        self.migrate()
        old_actual = doc_hashes.build_manifest(self.root, "1.0.0")
        before = self.manifest_path.read_bytes()
        with self.assertRaisesRegex(ValueError, "Narrow writes cannot change"):
            doc_hashes.refresh_source_hash_manifest(self.root, old_actual, ["SRC-SAMPLE"])
        self.assertEqual(before, self.manifest_path.read_bytes())

    def test_migration_rejects_duplicate_entries_and_changed_identity(self) -> None:
        for manifest in (
            {**self.legacy, "modules": self.legacy["modules"] * 2},
            {**self.legacy, "modules": [{**self.legacy["modules"][0], "path": "src/Other"}]},
        ):
            self.manifest_path.write_text(json.dumps(manifest))
            before = self.manifest_path.read_bytes()
            with self.assertRaises(ValueError):
                doc_hashes.migrate_reviewed_readmes(self.root)
            self.assertEqual(before, self.manifest_path.read_bytes())

    def test_validation_rejects_unknown_schema(self) -> None:
        self.manifest_path.write_text(json.dumps({**self.legacy, "schema": {"id": "meridian.source-doc-hashes", "version": "999"}}))
        with self.assertRaises(ValueError):
            doc_hashes.build_manifest(self.root)

if __name__ == "__main__":
    unittest.main()
