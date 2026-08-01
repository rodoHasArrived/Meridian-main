import importlib.util
import shutil
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


def _registry_root(temp_dir: str, sequence_line: str | None) -> Path:
    """Clone the real registries into a temp root, optionally injecting a sequence on one item."""
    root = Path(temp_dir)
    shutil.copytree(ROOT / "docs" / "roadmap" / "data", root / "docs" / "roadmap" / "data")
    (root / "docs" / "source" / "data").mkdir(parents=True)
    shutil.copy(
        ROOT / "docs" / "source" / "data" / "source-modules.yml",
        root / "docs" / "source" / "data" / "source-modules.yml",
    )
    if sequence_line is not None:
        items_path = root / "docs" / "roadmap" / "data" / "roadmap-items.yml"
        text = items_path.read_text(encoding="utf-8")
        marker = "  - id: W1-DATA-001\n"
        assert marker in text
        items_path.write_text(text.replace(marker, marker + sequence_line, 1), encoding="utf-8")
    return root


class RoadmapSourceDocsTests(unittest.TestCase):
    def test_current_registries_validate(self) -> None:
        self.assertEqual([], [finding for finding in validate_roadmap.validate(ROOT) if finding.severity == "error"])
        self.assertEqual([], [finding for finding in validate_source.validate(ROOT) if finding.severity == "error"])
        self.assertEqual([], [finding for finding in scan_todos.validate(ROOT) if finding.severity == "error"])

    def test_valid_sequence_passes_registry_validation(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = _registry_root(temp_dir, "    sequence: 3\n")
            errors = [finding for finding in validate_roadmap.validate(root) if finding.severity == "error"]

        self.assertEqual([], errors)

    def test_out_of_range_or_wrong_typed_sequence_fails_registry_validation(self) -> None:
        # The JSON Schema bounds sequence at integer >= 1 but nothing loads it, so without this
        # check the renderers would silently fall back to the identifier suffix — ordering a
        # generated view against its adopted rank while CI reported the data valid.
        for declared in ("0", "-2", "abc", "1.5", "true"):
            with self.subTest(sequence=declared):
                with tempfile.TemporaryDirectory() as temp_dir:
                    root = _registry_root(temp_dir, f"    sequence: {declared}\n")
                    errors = [finding for finding in validate_roadmap.validate(root) if finding.severity == "error"]

                self.assertTrue(
                    any("invalid sequence" in finding.message for finding in errors),
                    f"expected an invalid-sequence error for {declared!r}, got {[f.message for f in errors]}",
                )

    def test_wave_disagreeing_with_its_identifier_fails_registry_validation(self) -> None:
        # The sort key parses the wave from the identifier while the diagram labels with the
        # declared `wave`, so a mismatch would place an item among one wave's work while
        # presenting it as another's.
        with tempfile.TemporaryDirectory() as temp_dir:
            root = _registry_root(temp_dir, None)
            items_path = root / "docs" / "roadmap" / "data" / "roadmap-items.yml"
            text = items_path.read_text(encoding="utf-8")
            items_path.write_text(
                text.replace("  - id: W1-DATA-001\n    title: Provider trust gate and data confidence baseline\n    wave: W1\n",
                             "  - id: W1-DATA-001\n    title: Provider trust gate and data confidence baseline\n    wave: W2\n", 1),
                encoding="utf-8",
            )
            errors = [finding for finding in validate_roadmap.validate(root) if finding.severity == "error"]

        self.assertTrue(
            any("declares wave W2 but its identifier belongs to W1" in finding.message for finding in errors),
            [finding.message for finding in errors],
        )

    def test_generated_roadmap_views_share_one_ordering(self) -> None:
        # The diagram, summary, and register must not disagree about delivery sequence.
        items = [
            {"id": "W10-CONSOL-001", "sequence": 11},
            {"id": "W2-TRD-001"},
            {"id": "W10-MARK-001", "sequence": 1},
            {"id": "W9-ASSET-010"},
            {"id": "W9-TRUTH-001"},
        ]

        ordered = [item["id"] for item in sorted(items, key=common.roadmap_item_sort_key)]

        self.assertEqual(
            ["W2-TRD-001", "W9-TRUTH-001", "W9-ASSET-010", "W10-MARK-001", "W10-CONSOL-001"],
            ordered,
        )

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

    def test_doc_hash_manifest_narrow_write_preserves_unselected_modules(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/source/data").mkdir(parents=True)
            (root / "docs/source/generated").mkdir(parents=True)
            (root / "src/ExampleA").mkdir(parents=True)
            (root / "src/ExampleB").mkdir(parents=True)
            (root / "src/ExampleA/ExampleA.cs").write_text("namespace ExampleA;\n", encoding="utf-8")
            (root / "src/ExampleB/ExampleB.cs").write_text("namespace ExampleB;\n", encoding="utf-8")
            (root / "src/ExampleA/README.md").write_text("# Example A\n", encoding="utf-8")
            (root / "src/ExampleB/README.md").write_text("# Example B\n", encoding="utf-8")
            (root / "docs/source/data/source-modules.yml").write_text(
                """
schema:
  id: meridian.source-modules
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
modules:
  - id: SRC-EXAMPLE-A
    path: src/ExampleA
    name: Example A
    layer: Example
    status: active
    owner_lane: Example Lane
    purpose: Example module purpose.
    readme: src/ExampleA/README.md
    roadmap_items: []
    validation: []
    diagrams: []
    last_reviewed: 2026-05-20
  - id: SRC-EXAMPLE-B
    path: src/ExampleB
    name: Example B
    layer: Example
    status: active
    owner_lane: Example Lane
    purpose: Example module purpose.
    readme: src/ExampleB/README.md
    roadmap_items: []
    validation: []
    diagrams: []
    last_reviewed: 2026-05-20
""",
                encoding="utf-8",
            )
            baseline = doc_hashes.build_manifest(root)
            manifest_path = root / "docs/source/generated/source-hash-manifest.json"
            manifest_path.write_text(common.json.dumps(baseline, indent=2, sort_keys=True), encoding="utf-8")

            (root / "src/ExampleA/ExampleA.cs").write_text("namespace ExampleA;\npublic sealed class NewType {}\n", encoding="utf-8")
            (root / "src/ExampleB/ExampleB.cs").write_text("namespace ExampleB;\npublic sealed class UnreviewedType {}\n", encoding="utf-8")
            actual = doc_hashes.build_manifest(root)

            changed, refreshed = doc_hashes.refresh_source_hash_manifest(root, actual, ["SRC-EXAMPLE-A"])
            updated = common.json.loads(manifest_path.read_text(encoding="utf-8"))
            updated_by_id = {entry["id"]: entry for entry in updated["modules"]}
            baseline_by_id = {entry["id"]: entry for entry in baseline["modules"]}
            actual_by_id = {entry["id"]: entry for entry in actual["modules"]}

        self.assertTrue(changed)
        self.assertEqual(1, refreshed)
        self.assertEqual(actual_by_id["SRC-EXAMPLE-A"]["source_hash"], updated_by_id["SRC-EXAMPLE-A"]["source_hash"])
        self.assertEqual(baseline_by_id["SRC-EXAMPLE-B"]["source_hash"], updated_by_id["SRC-EXAMPLE-B"]["source_hash"])

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
