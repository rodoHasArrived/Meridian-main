import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def load_script(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.path.insert(0, str(path.parent))
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


validate_roadmap = load_script("validate_roadmap_registry", ROOT / "build" / "scripts" / "docs" / "validate-roadmap-registry.py")
validate_source = load_script("validate_source_readmes", ROOT / "build" / "scripts" / "docs" / "validate-source-readmes.py")
scan_todos = load_script("scan_source_todos", ROOT / "build" / "scripts" / "docs" / "scan-source-todos.py")
render_source = load_script("render_source_docs", ROOT / "build" / "scripts" / "docs" / "render-source-docs.py")
sync_source = load_script("sync_source_readmes", ROOT / "build" / "scripts" / "docs" / "sync-source-readmes.py")
doc_hashes = load_script("validate_doc_hashes", ROOT / "build" / "scripts" / "docs" / "validate-doc-hashes.py")
mark_stale = load_script("mark_stale_docs", ROOT / "build" / "scripts" / "docs" / "mark-stale-docs.py")
common = load_script("docs_common", ROOT / "build" / "scripts" / "docs" / "common.py")


class RoadmapSourceDocsTests(unittest.TestCase):
    def test_current_registries_validate(self) -> None:
        self.assertEqual([], [finding for finding in validate_roadmap.validate(ROOT) if finding.severity == "error"])
        self.assertEqual([], [finding for finding in validate_source.validate(ROOT) if finding.severity == "error"])
        self.assertEqual([], [finding for finding in scan_todos.validate(ROOT) if finding.severity == "error"])

    def test_generated_block_replacement_is_exact(self) -> None:
        original = "A\n<!-- begin -->\nold\n<!-- end -->\nB\n"
        updated, replaced = common.replace_marked_block(original, "<!-- begin -->", "<!-- end -->", "new")

        self.assertTrue(replaced)
        self.assertEqual("A\n<!-- begin -->\nnew\n<!-- end -->\nB\n", updated)

    def test_missing_source_readme_front_matter_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/source/data").mkdir(parents=True)
            (root / "src/Example").mkdir(parents=True)
            (root / "src/Example/README.md").write_text("# Example\n", encoding="utf-8")
            (root / "docs/source/data/source-modules.yml").write_text(
                """
schema:
  id: meridian.source-modules
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
modules:
  - id: SRC-EXAMPLE
    path: src/Example
    name: Example
    layer: Example
    status: active
    owner_lane: Example
    purpose: Example
    readme: src/Example/README.md
    roadmap_items: []
    validation: []
    last_reviewed: 2026-05-20
""",
                encoding="utf-8",
            )
            (root / "docs/source/data/source-readme-coverage.yml").write_text(
                """
schema:
  id: meridian.readme-coverage
  version: "1.0.0"
modules:
  - module_id: SRC-EXAMPLE
    path: src/Example
    readme: src/Example/README.md
    status: covered
""",
                encoding="utf-8",
            )

            errors = [finding for finding in validate_source.validate(root) if finding.severity == "error"]

        self.assertTrue(any("missing front matter key" in finding.message for finding in errors))

    def test_source_readme_blocks_render_from_registry(self) -> None:
        modules, todos, roadmap = render_source.module_maps(ROOT)
        dashboard = next(module for module in modules["modules"] if module["id"] == "SRC-UI-DASHBOARD")

        trace, checklist = render_source.readme_blocks(dashboard, todos, roadmap)

        self.assertIn("W2-TRD-001", trace)
        self.assertIn("TODO-SRC-UI-DASHBOARD-001", checklist)

    def test_sync_source_readmes_creates_missing_readme_from_registry(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/source/data").mkdir(parents=True)
            (root / "src/Example").mkdir(parents=True)
            (root / "docs/source/data/source-modules.yml").write_text(
                """
schema:
  id: meridian.source-modules
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
modules:
  - id: SRC-EXAMPLE
    path: src/Example
    name: Example
    layer: Example
    status: active
    owner_lane: Example Lane
    purpose: Example module purpose.
    readme: src/Example/README.md
    roadmap_items: []
    validation:
      - python3 example.py
    diagrams: []
    last_reviewed: 2026-05-20
""",
                encoding="utf-8",
            )

            changed, _ = sync_source.sync(root, create_missing=True)
            readme = root / "src/Example/README.md"
            exists = readme.exists()
            content = readme.read_text(encoding="utf-8")

        self.assertEqual(1, changed)
        self.assertTrue(exists)
        self.assertIn("## Optional conditional sections", content)
        self.assertIn("### End-user value", content)

    def test_tree_readme_sync_respects_ignore_patterns(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/source/data").mkdir(parents=True)
            (root / "src/Example/Feature").mkdir(parents=True)
            (root / "src/Example/bin/Debug").mkdir(parents=True)
            (root / "src/Example/Feature/Feature.cs").write_text("namespace Example;\n", encoding="utf-8")
            (root / "src/Example/bin/Debug/Generated.cs").write_text("namespace Example;\n", encoding="utf-8")
            (root / "docs/source/data/source-readme-ignore.yml").write_text(
                """
schema:
  id: meridian.source-readme-ignore
  version: "1.0.0"
tree_roots:
  - src/Example/Feature
patterns:
  - "**/bin/**"
""",
                encoding="utf-8",
            )
            (root / "docs/source/data/source-modules.yml").write_text(
                """
schema:
  id: meridian.source-modules
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
modules:
  - id: SRC-EXAMPLE
    path: src/Example
    name: Example
    layer: Example
    status: active
    owner_lane: Example Lane
    purpose: Example module purpose.
    readme: src/Example/README.md
    roadmap_items: []
    validation: []
    diagrams: []
    last_reviewed: 2026-05-20
""",
                encoding="utf-8",
            )

            changed, _ = sync_source.sync(root, create_missing=True, tree=True, max_depth=2)

            self.assertEqual(2, changed)
            self.assertTrue((root / "src/Example/Feature/README.md").exists())
            self.assertFalse((root / "src/Example/bin/Debug/README.md").exists())

    def test_doc_hash_manifest_contains_registered_modules(self) -> None:
        manifest = doc_hashes.build_manifest(ROOT)
        module_ids = {entry["id"] for entry in manifest["modules"]}

        self.assertIn("SRC-HOST", module_ids)
        self.assertIn("SRC-UI-DASHBOARD", module_ids)

    def test_stale_doc_marker_reports_source_hash_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/source/data").mkdir(parents=True)
            (root / "docs/source/generated").mkdir(parents=True)
            (root / "src/Example").mkdir(parents=True)
            (root / "src/Example/Example.cs").write_text("namespace Example;\n", encoding="utf-8")
            (root / "src/Example/README.md").write_text(
                """
---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-EXAMPLE
path: src/Example
status: active
owner_lane: Example Lane
last_reviewed: 2026-05-20
---

# Example
""".lstrip(),
                encoding="utf-8",
            )
            (root / "docs/source/data/source-modules.yml").write_text(
                """
schema:
  id: meridian.source-modules
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
modules:
  - id: SRC-EXAMPLE
    path: src/Example
    name: Example
    layer: Example
    status: active
    owner_lane: Example Lane
    purpose: Example module purpose.
    readme: src/Example/README.md
    roadmap_items: []
    validation: []
    diagrams: []
    last_reviewed: 2026-05-20
""",
                encoding="utf-8",
            )
            baseline = doc_hashes.build_manifest(root)
            (root / "docs/source/generated/source-hash-manifest.json").write_text(
                common.json.dumps(baseline, indent=2, sort_keys=True) if hasattr(common, "json") else __import__("json").dumps(baseline, indent=2, sort_keys=True),
                encoding="utf-8",
            )
            (root / "src/Example/Example.cs").write_text("namespace Example;\npublic sealed class ExampleType {}\n", encoding="utf-8")

            report = mark_stale.build_stale_report(root)

        self.assertEqual(1, report["stale_count"])
        self.assertEqual("SRC-EXAMPLE", report["stale_modules"][0]["module_id"])
        self.assertIn("source_hash_drift", report["stale_modules"][0]["reasons"])


if __name__ == "__main__":
    unittest.main()
