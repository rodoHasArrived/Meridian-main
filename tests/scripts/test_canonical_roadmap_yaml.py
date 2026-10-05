"""Canonical roadmap data must be parsed as standard YAML by every entry point."""

from __future__ import annotations

import builtins
import importlib.util
import locale
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import yaml

ROOT = Path(__file__).resolve().parents[2]
DOCS_SCRIPTS = ROOT / "build" / "scripts" / "docs"
CANONICAL_NAMES = {
    "decision-log.yml", "document-index.yml", "program-state.yml",
    "risk-register.yml", "roadmap-items.yml", "stage-gates.yml",
}
MALFORMED = "schema:\n  id: meridian.roadmap-items\nitems:\n  - current_summary: Review: pending\n"


def load_script(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


common = load_script("canonical_yaml_common", DOCS_SCRIPTS / "common.py")
normalizer = load_script("canonical_yaml_normalizer", ROOT / "tools" / "roadmap" / "render_roadmap_docs.py")


def without_yaml(name, *args, **kwargs):
    if name == "yaml":
        raise ModuleNotFoundError("No module named 'yaml'", name="yaml")
    return REAL_IMPORT(name, *args, **kwargs)


REAL_IMPORT = builtins.__import__


class CanonicalRoadmapYamlTests(unittest.TestCase):
    def test_all_canonical_registries_use_standard_parser(self) -> None:
        paths = list((ROOT / "docs" / "roadmap" / "data").glob("*.yml"))
        self.assertEqual(CANONICAL_NAMES, {path.name for path in paths})
        original = yaml.safe_load
        with mock.patch.object(yaml, "safe_load", wraps=original) as parser:
            for path in paths:
                with self.subTest(registry=path.name):
                    self.assertEqual(original(path.read_text(encoding="utf-8")), common.load_data(path))
        self.assertEqual(len(CANONICAL_NAMES), parser.call_count)

    def test_malformed_yaml_includes_path_line_and_column(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "docs" / "roadmap" / "data" / "roadmap-items.yml"
            path.parent.mkdir(parents=True)
            path.write_text(MALFORMED, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, r"roadmap-items\.yml:4:28: malformed YAML"):
                common.load_data(path)

    def test_missing_parser_dependency_is_explicit(self) -> None:
        path = ROOT / "docs" / "roadmap" / "data" / "roadmap-items.yml"
        with mock.patch("builtins.__import__", side_effect=without_yaml):
            with self.assertRaises(RuntimeError) as failure:
                common.load_data(path)
        message = str(failure.exception)
        self.assertIn(str(path), message)
        self.assertIn("requires PyYAML", message)
        self.assertIn("build/scripts/docs/requirements.txt", message)

    def test_json_loading_does_not_require_yaml_dependency(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "fixture.json"
            path.write_text('{"summary": "Review: pending"}', encoding="utf-8")
            with mock.patch("builtins.__import__", side_effect=without_yaml):
                self.assertEqual({"summary": "Review: pending"}, common.load_data(path))

    def test_valid_colon_hash_and_folded_text_survive_unchanged(self) -> None:
        content = 'quoted: "Review: pending #2948"\nfolded: >-\n  Review: pending\n  and repair: required\n'
        expected = {"quoted": "Review: pending #2948", "folded": "Review: pending and repair: required"}
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "roadmap.yml"
            path.write_text(content, encoding="utf-8")
            self.assertEqual(expected, common.load_data(path))

    def test_existing_ingest_summary_retains_literal_pr_number_and_suffix(self) -> None:
        data = common.load_data(ROOT / "docs" / "roadmap" / "data" / "roadmap-items.yml")
        item = next(item for item in data["items"] if item["id"] == "W9-INGEST-009")
        self.assertIn("#2948. This remains shared-service/file-store evidence", item["current_summary"])
        self.assertTrue(item["current_summary"].endswith("does not close operator acceptance."))

    def test_missing_yaml_parser_does_not_use_legacy_recovery(self) -> None:
        path = ROOT / "docs" / "roadmap" / "data" / "roadmap-items.yml"
        with mock.patch("builtins.__import__", side_effect=without_yaml):
            with self.assertRaises(RuntimeError):
                normalizer.render(str(path), "unused.yaml", set())

    def test_normalizer_preserves_canonical_registry_values(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            for name in sorted(CANONICAL_NAMES):
                with self.subTest(registry=name):
                    source = ROOT / "docs" / "roadmap" / "data" / name
                    output = Path(tmp) / name
                    normalizer.render(str(source), str(output), set())
                    self.assertEqual(common.load_data(source), common.load_data(output))

    def test_normalizer_preserves_scalar_types_and_quoted_text(self) -> None:
        expected = {"summary": "Review: pending #2948", "numeric_text": "01", "enabled": True,
                    "count": 2, "ratio": 1.5, "optional": None, "items": []}
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "source.yml"
            output = Path(tmp) / "output.yml"
            source.write_text(yaml.safe_dump(expected), encoding="utf-8")
            normalizer.render(str(source), str(output), set())
            self.assertEqual(expected, yaml.safe_load(output.read_text(encoding="utf-8")))

    def test_normalizer_preserves_caller_locale_timezone_and_unicode(self) -> None:
        original_locale = locale.setlocale(locale.LC_ALL)
        self.addCleanup(locale.setlocale, locale.LC_ALL, original_locale)
        with tempfile.TemporaryDirectory() as tmp, mock.patch.dict(os.environ, {"TZ": "Pacific/Honolulu"}):
            source = Path(tmp) / "source.yml"
            output = Path(tmp) / "output.yml"
            source.write_text('summary: "Cafe\u0301 – re\u0301sume\u0301"\n', encoding="utf-8")

            normalizer.render(str(source), str(output), set())

            self.assertEqual(original_locale, locale.setlocale(locale.LC_ALL))
            self.assertEqual("Pacific/Honolulu", os.environ["TZ"])
            self.assertEqual({"summary": "Café – résumé"}, yaml.safe_load(output.read_text(encoding="utf-8")))

    def test_failed_normalizer_preserves_caller_locale_and_timezone(self) -> None:
        original_locale = locale.setlocale(locale.LC_ALL)
        self.addCleanup(locale.setlocale, locale.LC_ALL, original_locale)
        with tempfile.TemporaryDirectory() as tmp, mock.patch.dict(os.environ, {"TZ": "Pacific/Honolulu"}):
            source = Path(tmp) / "source.yml"
            output = Path(tmp) / "output.yml"
            source.write_text(MALFORMED, encoding="utf-8")

            with self.assertRaises(ValueError):
                normalizer.render(str(source), str(output), set())

            self.assertEqual(original_locale, locale.setlocale(locale.LC_ALL))
            self.assertEqual("Pacific/Honolulu", os.environ["TZ"])
            self.assertFalse(output.exists())

    def test_malformed_yaml_fails_every_validation_and_render_entry_point(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            shutil.copytree(ROOT / "docs" / "roadmap" / "data", root / "docs" / "roadmap" / "data")
            shutil.copytree(ROOT / "docs" / "source" / "data", root / "docs" / "source" / "data")
            path = root / "docs" / "roadmap" / "data" / "roadmap-items.yml"
            path.write_text(MALFORMED, encoding="utf-8")
            output = root / "normalized.yml"
            commands = [
                [str(DOCS_SCRIPTS / "validate-roadmap-registry.py"), "--root", str(root), "--summary"],
                [str(DOCS_SCRIPTS / "render-roadmap-docs.py"), "--root", str(root), "--summary"],
                [str(DOCS_SCRIPTS / "render-roadmap-diagrams.py"), "--root", str(root), "--summary"],
                [str(DOCS_SCRIPTS / "render-source-docs.py"), "--root", str(root), "--summary"],
                [str(ROOT / "tools" / "roadmap" / "validate_roadmap.py"), "--roadmap", str(path)],
                [str(ROOT / "tools" / "roadmap" / "render_roadmap_docs.py"), str(path), str(output)],
            ]
            for command in commands:
                with self.subTest(script=Path(command[0]).name):
                    result = subprocess.run([sys.executable, *command], capture_output=True, text=True, check=False)
                    self.assertNotEqual(0, result.returncode, result.stdout)
                    self.assertIn("roadmap-items.yml:4:28", result.stderr)
                    self.assertIn("malformed YAML", result.stderr)
            self.assertFalse(output.exists())
            self.assertFalse((root / "docs" / "roadmap" / "generated").exists())

    def test_malformed_input_cannot_overwrite_existing_normalized_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "source.yml"
            output = Path(tmp) / "output.yml"
            source.write_text(MALFORMED, encoding="utf-8")
            output.write_text("existing: retained\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                normalizer.render(str(source), str(output), set())
            self.assertEqual("existing: retained\n", output.read_text(encoding="utf-8"))

    def test_workflows_install_pinned_parser_before_yaml_consumers(self) -> None:
        directory = ROOT / ".github" / "workflows"
        expected = {
            "roadmap-source-docs.yml": ("schema", "Validate roadmap registry"),
            "roadmap-tools-manual.yml": ("roadmap-tools", "Validate fixture enums"),
            "targeted-test.yml": ("targeted", "Run docs/source validation"),
        }
        for filename, (job, consumer) in expected.items():
            with self.subTest(workflow=filename):
                data = yaml.safe_load((directory / filename).read_text(encoding="utf-8"))
                steps = data["jobs"][job]["steps"]
                install = next(index for index, step in enumerate(steps)
                               if step.get("name") == "Install documentation parser dependencies")
                consume = next(index for index, step in enumerate(steps) if step.get("name") == consumer)
                self.assertLess(install, consume)
                self.assertIn("--requirement build/scripts/docs/requirements.txt", steps[install]["run"])


if __name__ == "__main__":
    unittest.main()
