"""Strict fixtures for authored template-entry discovery, including legacy names."""
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/check_design_system_governance.py"
spec = importlib.util.spec_from_file_location("entry_governance", SCRIPT)
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)

ENTRY = '''<!doctype html><html lang="en"><head><title>Fixture</title></head><body><x-dc>
<!-- @template name="Fixture" description="Entry discovery fixture" -->
<h1>Fixture</h1><x-import component-from-global-scope="DS.DataStateSwitch"></x-import></x-dc>
<script data-props='{"$preview":{},"density":{},"theme":{},"environment":{},"dataState":{}}'></script>
</body></html>'''


class DeclaredEntryGovernanceTests(unittest.TestCase):
    def check(self, entries, files):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "api").mkdir()
            (root / "api/template-eligibility.json").write_text(json.dumps({"templates": {"legacy-example": {"entries": entries}}}))
            folder = root / "templates/legacy-example"
            folder.mkdir(parents=True)
            (folder / "ds-base.js").write_text("// Fixture loader")
            for filename, content in files.items():
                (folder / filename).write_text(content)
            audit = gate.Audit(root, baseline={})
            audit.template_checks()
            return audit.failures

    def test_declared_legacy_filename_is_valid(self):
        self.assertEqual([], self.check(["Legacy Example.dc.html"], {"Legacy Example.dc.html": ENTRY}))

    def test_declared_missing_entry_fails(self):
        self.assertTrue(any("declared entry Missing.dc.html missing" in result.detail for result in self.check(["Missing.dc.html"], {})))

    def test_undeclared_entry_fails(self):
        result = self.check(["First.dc.html"], {"First.dc.html": ENTRY, "Extra.dc.html": ENTRY})
        self.assertTrue(any("undeclared entry Extra.dc.html" in item.detail for item in result))

    def test_duplicate_entry_fails(self):
        result = self.check(["First.dc.html", "First.dc.html"], {"First.dc.html": ENTRY})
        self.assertTrue(any("duplicate declared entry" in item.detail for item in result))

    def test_traversal_filename_fails(self):
        result = self.check(["../Outside.dc.html"], {})
        self.assertTrue(any("invalid entry filename" in item.detail for item in result))

    def test_second_entry_keeps_full_contract_enforcement(self):
        result = self.check(["First.dc.html", "Second.dc.html"], {
            "First.dc.html": ENTRY,
            "Second.dc.html": ENTRY.replace('"theme":{},', '').replace("DS.DataStateSwitch", "DS.Widget"),
        })
        self.assertTrue(any(item.path.endswith("Second.dc.html") and "Standard tweak 'theme' missing" in item.detail for item in result))
        self.assertTrue(any(item.path.endswith("Second.dc.html") and "DataStateSwitch not mounted" in item.detail for item in result))


if __name__ == "__main__":
    unittest.main()
