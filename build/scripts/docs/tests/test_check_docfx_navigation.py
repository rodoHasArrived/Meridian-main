"""Publication regressions: source presence is insufficient without a build mapping."""

import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
spec = importlib.util.spec_from_file_location("check_docfx_navigation", SCRIPTS / "check-docfx-navigation.py")
navigation = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = navigation
spec.loader.exec_module(navigation)


class NavigationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.config = {
            "metadata": [{"dest": "docs/api"}],
            "build": {
                "content": [{"src": ".", "files": ["docs/**/*.md", "docs/**/toc.yml"]}],
                "resource": [],
            },
        }
        self.write("docs/toc.yml", "- name: Home\n  href: README.md\n")
        self.write("docs/README.md", "# Home\n")

    def write(self, name, content=""):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def check(self, *, site=False):
        self.write("docfx.json", json.dumps(self.config))
        return navigation.check_navigation(self.root / "docfx.json", self.root / "site" if site else None)

    def test_resource_registry_keeps_yaml_extension_in_output(self):
        self.write("docs/toc.yml", "- href: data/registry.yml\n")
        self.write("docs/data/registry.yml", "records: []\n")
        self.assertTrue(any("excluded" in error for error in self.check().errors))
        self.config["build"]["resource"].append({"files": ["docs/data/*.yml"]})
        self.assertEqual([], self.check().errors)
        self.write("site/docs/toc.html")
        self.write("site/docs/data/registry.html")
        self.assertTrue(any("docs/data/registry.yml" in error for error in self.check(site=True).errors))
        self.write("site/docs/data/registry.yml")
        self.assertEqual([], self.check(site=True).errors)

    def test_missing_and_excluded_targets_are_distinct_errors(self):
        self.write("docs/toc.yml", "- href: missing.md\n- href: draft.md\n")
        self.write("docs/draft.md", "# Draft\n")
        self.config["build"]["content"][0]["exclude"] = ["docs/draft.md"]
        errors = self.check().errors
        self.assertTrue(any("missing.md: source file is missing" in error for error in errors))
        self.assertTrue(any("draft.md: excluded" in error for error in errors))

    def test_nested_toc_paths_are_relative_to_each_toc_and_accept_queries(self):
        self.write("docs/toc.yml", "- href: guides/toc.yml\n  homepage: guides/intro.md\n")
        self.write("docs/guides/intro.md", "# Intro\n")
        self.write("docs/guides/toc.yml", "items:\n- href: ../README.md#home\n- topicHref: intro.md?mode=one\n")
        self.assertEqual([], self.check().errors)
        (self.root / "docs/guides/intro.md").unlink()
        self.assertEqual(2, sum("source file is missing" in error for error in self.check().errors))

    def test_directory_toc_and_percent_encoded_file_names(self):
        self.write("docs/toc.yml", "- href: guides/\n")
        self.write("docs/guides/toc.yml", "- href: My%20Guide.md\n")
        self.write("docs/guides/My Guide.md", "# Guide\n")
        self.assertEqual([], self.check().errors)

    def test_src_and_dest_mappings_determine_output_location(self):
        self.config["build"]["content"] = [{"src": "docs", "dest": "manual", "files": ["**/*.md", "**/toc.yml"]}]
        self.assertEqual([], self.check().errors)
        self.write("site/manual/toc.html")
        self.write("site/manual/README.html")
        self.assertEqual([], self.check(site=True).errors)
        (self.root / "site/manual/README.html").unlink()
        self.assertTrue(any("manual/README.html" in error for error in self.check(site=True).errors))

    def test_external_urls_and_fragments_are_out_of_scope(self):
        self.write("docs/toc.yml", "- href: https://example.com/missing.md\n- href: '#section'\n- href: //example.com/guide\n")
        self.assertEqual([], self.check().errors)

    def test_source_mode_defers_only_mapped_generated_yaml(self):
        self.write("docs/toc.yml", "- href: api/toc.yml\n  homepage: api/index.md\n")
        result = self.check()
        self.assertEqual({"docs/api/toc.yml"}, result.deferred)
        self.assertTrue(any("index.md: source file is missing" in error for error in result.errors))
        self.write("docs/api/index.md", "# API\n")
        self.assertEqual([], self.check().errors)
        self.config["build"]["content"][0]["exclude"] = ["docs/api/**"]
        self.assertTrue(any("api/toc.yml: excluded" in error for error in self.check().errors))

    def test_site_mode_requires_generated_output(self):
        self.write("docs/toc.yml", "- href: api/toc.yml\n")
        self.write("site/docs/toc.html")
        result = self.check(site=True)
        self.assertEqual(set(), result.deferred)
        self.assertTrue(any("docs/api/toc.html" in error for error in result.errors))
        self.write("site/docs/api/toc.html")
        self.assertEqual([], self.check(site=True).errors)

    def test_local_archive_links_require_published_content(self):
        self.write("docs/README.md", "[History](../archive/docs/old.md#context)\n")
        self.write("archive/docs/old.md", "# Old\n")
        self.assertTrue(any("excluded" in error for error in self.check().errors))
        self.config["build"]["content"].append({"files": ["archive/docs/**/*.md"]})
        self.assertEqual([], self.check().errors)
        (self.root / "archive/docs/old.md").unlink()
        self.assertTrue(any("source file is missing" in error for error in self.check().errors))

    def test_archive_references_are_checked_but_code_examples_are_ignored(self):
        self.write("docs/README.md", """# Home
```md
[Example](../archive/docs/example.md)
```
`[Example](../archive/docs/example.md)`
[Old][history]
[history]: ../archive/docs/old.md "History"
""")
        errors = self.check().errors
        self.assertTrue(any("old.md" in error for error in errors))
        self.assertFalse(any("example.md" in error for error in errors))

    def test_no_included_toc_is_an_error(self):
        self.config["build"]["content"][0]["exclude"] = ["**/toc.yml"]
        self.assertIn("No TOC files are included in DocFX content", self.check().errors)

    def test_invalid_yaml_is_an_error(self):
        self.write("docs/toc.yml", "items: [\n")
        self.assertTrue(any("malformed YAML" in error for error in self.check().errors))

    def test_glob_directory_semantics(self):
        for name in ("docs/README.md", "docs/nested/README.md"):
            self.assertTrue(navigation.glob_matches(name, "docs/**/*.md"))
        self.assertFalse(navigation.glob_matches("docs/nested/README.md", "docs/*.md"))
        self.assertFalse(navigation.glob_matches("docs/nested/toc.yml", "docs/**/*.md"))


if __name__ == "__main__":
    unittest.main()
