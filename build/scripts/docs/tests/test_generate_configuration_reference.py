#!/usr/bin/env python3
"""Regression tests for source-backed sample configuration documentation."""

from __future__ import annotations

import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO


SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIR))
SPEC = importlib.util.spec_from_file_location(
    "configuration_reference_under_test", SCRIPT_DIR / "generate-configuration-reference.py"
)
assert SPEC is not None and SPEC.loader is not None
reference = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = reference
SPEC.loader.exec_module(reference)


class ConfigurationReferenceTests(unittest.TestCase):
    def test_jsonc_strings_comments_arrays_and_source_lines(self) -> None:
        sample = r'''{
  // HTTP destination, preserving // inside its value.
  "Api": {
    "Url": "https://example.test/a//b",
    "Quoted": "escaped \"quote\" and \\ slash",
    /* a block comment spanning
       two lines */
    "Sources": [{"Id": "primary", "Enabled": false}],
    "Empty": [],
    "Optional": null
  }
}'''
        entries = reference.sample_entries(sample)
        by_path = {entry.path: entry for entry in entries}
        self.assertEqual(
            ["Api", "Api.Url", "Api.Quoted", "Api.Sources", "Api.Sources[0].Id",
             "Api.Sources[0].Enabled", "Api.Empty", "Api.Optional"],
            list(by_path),
        )
        self.assertEqual("https://example.test/a//b", by_path["Api.Url"].value)
        self.assertEqual(8, by_path["Api.Sources"].line)
        self.assertEqual(8, by_path["Api.Sources[0].Enabled"].line)
        self.assertEqual(
            "HTTP destination, preserving // inside its value.", by_path["Api"].note
        )
        self.assertEqual("boolean", reference.json_type(False))
        self.assertEqual("integer", reference.json_type(0))
        self.assertEqual("number", reference.json_type(0.5))
        self.assertEqual("null", reference.json_type(None))

    def test_invalid_or_ambiguous_samples_fail_instead_of_empty_output(self) -> None:
        for sample in ("{}", "[]", '{"A": 1, "A": 2}', '{"A": {"B": 1, "B": 2}}',
                       '{"A": /* unfinished', '{"A": true,}'):
            with self.subTest(sample=sample), self.assertRaises(ValueError):
                reference.render(sample)

    def test_sensitive_values_and_markdown_are_handled(self) -> None:
        rendered = reference.render('''{
  "Provider": {
    // Example secret never belongs in the published reference: unsafe-secret.
    "ApiKey": "unsafe-value",
    "Description": "a|b `c` <script>"
  }
}''')
        self.assertNotIn("unsafe-value", rendered)
        self.assertNotIn("unsafe-secret", rendered)
        self.assertIn("[redacted]", rendered)
        self.assertIn("a&#124;b &#96;c&#96; &lt;script&gt;", rendered)
        self.assertIn("sample values, not missing-key runtime defaults", rendered)

    def test_only_sample_is_read_and_check_detects_drift_without_writing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "config").mkdir()
            sample = root / reference.SAMPLE_PATH
            sample.write_text('{"DataRoot": "example"}\n', encoding="utf-8")
            (root / "config/appsettings.json").write_text(
                '{"Password": "local-secret"}', encoding="utf-8"
            )
            with redirect_stdout(StringIO()), redirect_stderr(StringIO()):
                self.assertEqual(1, reference.main(["--root", str(root), "--check"]))
                self.assertFalse((root / reference.OUTPUT_PATH).exists())
                self.assertEqual(0, reference.main(["--root", str(root)]))
                output = root / reference.OUTPUT_PATH
                before = output.read_bytes()
                self.assertNotIn(b"local-secret", before)
                self.assertEqual(0, reference.main(["--root", str(root), "--check"]))
                self.assertEqual(before, output.read_bytes())
                sample.write_text('{"DataRoot": "changed"}\n', encoding="utf-8")
                self.assertEqual(1, reference.main(["--root", str(root), "--check"]))
                self.assertEqual(before, output.read_bytes())


if __name__ == "__main__":
    unittest.main()
