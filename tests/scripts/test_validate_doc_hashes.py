"""Source documentation hashes must survive checkout platform changes."""

from __future__ import annotations

import copy
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


REPO_ROOT = Path(__file__).resolve().parents[2]
DASHBOARD_PACKAGE = REPO_ROOT / "src/Meridian.Ui/dashboard/package.json"


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


class DependencyManifestFingerprintTests(unittest.TestCase):
    """Dependabot bumps must not drift reviewed hashes; real source edits must."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.module = self.root / "src/Dashboard"
        (self.module / "src").mkdir(parents=True)
        registry = self.root / "docs/source/data/source-modules.yml"
        registry.parent.mkdir(parents=True)
        registry.write_text(json.dumps({"modules": [{
            "id": "SRC-DASH", "path": "src/Dashboard", "readme": "src/Dashboard/README.md",
        }]}), encoding="utf-8")
        (self.module / "README.md").write_text("# Dashboard\n\nOperator workflow.\n", encoding="utf-8")
        self.app = self.module / "src/app.tsx"
        self.app.write_text("export const App = () => null;\n", encoding="utf-8")
        self.package_path = self.module / "package.json"
        self.lockfile = self.module / "package-lock.json"
        self.package = {
            "name": "@meridian/dashboard",
            "version": "0.1.0",
            "private": True,
            "scripts": {"build": "vite build"},
            "dependencies": {
                "react": "19.3.1",
                "local-tools": "file:../..",
                "legacy-alias": "npm:@scope/real-package@^1.2.0",
            },
            "devDependencies": {"jest-axe": "^10.0.0", "vitest": "5.0.5"},
            "optionalDependencies": {"fsevents": "~2.3.0"},
            "peerDependencies": {"react": ">=19"},
            "overrides": {"postcss": "8.5.31", "nanoid": {".": "^3.3.17", "dep": "1.0.0"}},
            "engines": {"node": "^24.15.0"},
        }
        write_json(self.package_path, self.package)
        self.lockfile.write_text('{"lockfileVersion": 3, "packages": {"node_modules/react": {"version": "19.3.1"}}}\n')
        self.manifest_path = self.root / doc_hashes.HASH_MANIFEST
        self.manifest_path.parent.mkdir(parents=True)
        self.manifest_path.write_text(json.dumps(doc_hashes.build_manifest(self.root)), encoding="utf-8")

    def source_drift(self) -> list:
        findings = doc_hashes.validate_source_hash_manifest(self.root, doc_hashes.build_manifest(self.root))
        return [finding for finding in findings if "source hash drift" in finding.message]

    def bump(self, **changes) -> dict:
        package = copy.deepcopy(self.package)
        for section, versions in changes.items():
            package[section].update(versions)
        return package

    def test_simulated_dependabot_bump_with_lockfile_refresh_does_not_drift(self) -> None:
        write_json(self.package_path, self.bump(
            dependencies={"react": "19.4.0", "legacy-alias": "npm:@scope/real-package@^1.3.0"},
            devDependencies={"jest-axe": "^11.0.0", "vitest": "5.1.0"},
            optionalDependencies={"fsevents": "~2.3.3"},
            overrides={"postcss": "8.5.32", "nanoid": {".": "^3.3.18", "dep": "$vitest"}},
        ))
        self.lockfile.write_text('{"lockfileVersion": 3, "packages": {"node_modules/react": {"version": "19.4.0"}}}\n')
        self.assertEqual(self.source_drift(), [])

    def test_lockfile_is_excluded_from_source_hash(self) -> None:
        before = doc_hashes.tree_hash(self.root, self.module)
        self.lockfile.write_text('{"lockfileVersion": 3, "packages": {"node_modules/other": {}}}\n')
        self.assertEqual(before, doc_hashes.tree_hash(self.root, self.module))
        self.lockfile.unlink()
        self.assertEqual(before, doc_hashes.tree_hash(self.root, self.module))

    def test_real_source_change_still_drifts(self) -> None:
        self.app.write_text("export const App = () => 'changed';\n", encoding="utf-8")
        self.assertEqual(len(self.source_drift()), 1)

    def test_version_bump_combined_with_source_change_still_drifts(self) -> None:
        write_json(self.package_path, self.bump(devDependencies={"vitest": "5.1.0"}))
        self.app.write_text("export const App = () => 'changed';\n", encoding="utf-8")
        self.assertEqual(len(self.source_drift()), 1)

    def test_package_json_changes_beyond_dependency_versions_still_drift(self) -> None:
        added = self.bump(dependencies={"left-pad": "1.3.0"})
        removed = copy.deepcopy(self.package)
        del removed["devDependencies"]["jest-axe"]
        moved = copy.deepcopy(self.package)
        moved["devDependencies"]["react"] = moved["dependencies"].pop("react")
        variants = {
            "added dependency": added,
            "removed dependency": removed,
            "dependency moved between sections": moved,
            "added override": self.bump(overrides={"zod": "^4.6.7"}),
            "alias target package": self.bump(dependencies={"legacy-alias": "npm:@scope/other-package@^1.2.0"}),
            "file dependency": self.bump(dependencies={"local-tools": "file:../../.."}),
            "git dependency": self.bump(dependencies={"react": "github:facebook/react#v19.3.1"}),
            "peer dependency range": self.bump(peerDependencies={"react": ">=20"}),
            "script": self.bump(scripts={"build": "tsc && vite build"}),
            "engines": self.bump(engines={"node": "^26.0.0"}),
            "package version": {**self.package, "version": "0.2.0"},
        }
        for name, package in variants.items():
            with self.subTest(change=name):
                write_json(self.package_path, package)
                self.assertEqual(len(self.source_drift()), 1)

    def test_malformed_or_ambiguous_package_json_hashes_raw_bytes(self) -> None:
        valid = json.dumps(self.package, indent=2)
        duplicate = valid.replace('"name":', '"name": "shadowed",\n  "name":', 1)
        for name, content in {
            "invalid JSON": valid + "\n}",
            "duplicate keys": duplicate,
            "non-object document": "[]\n",
        }.items():
            with self.subTest(case=name):
                self.assertIsNone(doc_hashes.package_manifest_fingerprint(content.encode()))
                self.package_path.write_text(content, encoding="utf-8")
                self.assertEqual(
                    doc_hashes.source_file_hash(self.package_path),
                    doc_hashes.sha256_manifest_file(self.package_path),
                )
                self.assertEqual(len(self.source_drift()), 1)

    def test_package_json_fingerprint_is_newline_portable(self) -> None:
        content = json.dumps(self.package, indent=2) + "\n"
        self.assertEqual(
            doc_hashes.package_manifest_fingerprint(content.encode()),
            doc_hashes.package_manifest_fingerprint(content.replace("\n", "\r\n").encode()),
        )

    def test_checked_in_dashboard_manifest_masks_every_registry_version(self) -> None:
        package = json.loads(DASHBOARD_PACKAGE.read_text(encoding="utf-8"))
        before = doc_hashes.package_manifest_fingerprint(DASHBOARD_PACKAGE.read_bytes())
        self.assertIsNotNone(before)
        bumped = copy.deepcopy(package)
        source_specs = []
        for section in doc_hashes.PACKAGE_VERSION_SECTIONS:
            for name, spec in bumped.get(section, {}).items():
                if isinstance(spec, str) and ":" not in spec and "/" not in spec:
                    bumped[section][name] = spec + "-next.1"
                else:
                    source_specs.append((section, name))
        self.assertEqual(before, doc_hashes.package_manifest_fingerprint(json.dumps(bumped, indent=2).encode()))
        # Local file dependencies (for example meridian-tools) still participate.
        for section, name in source_specs:
            with self.subTest(dependency=name):
                changed = copy.deepcopy(package)
                changed[section][name] = "file:../elsewhere"
                self.assertNotEqual(before, doc_hashes.package_manifest_fingerprint(json.dumps(changed).encode()))


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
