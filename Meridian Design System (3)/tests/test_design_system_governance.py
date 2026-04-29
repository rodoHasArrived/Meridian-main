import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "check_design_system_governance.py"

spec = importlib.util.spec_from_file_location("check_design_system_governance", SCRIPT)
governance = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = governance
spec.loader.exec_module(governance)


class DesignSystemGovernanceTests(unittest.TestCase):
    def run_on_files(self, files):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return governance.run_checks(root, baseline={})

    def test_clean_package_passes(self):
        violations = governance.run_checks(ROOT)
        self.assertEqual([], violations)

    def test_missing_local_link_is_reported(self):
        violations = self.run_on_files({"index.html": '<a href="missing.html">Broken</a>'})
        self.assertTrue(any(v.code == "local-link" for v in violations))

    def test_local_upload_link_is_reported_even_when_file_exists(self):
        violations = self.run_on_files(
            {
                "index.html": '<img src="uploads/reference.png" alt="">',
                "uploads/reference.png": "local-only image",
            }
        )
        self.assertTrue(any(v.code == "local-upload-reference" for v in violations))

    def test_raw_hex_is_reported_outside_baseline(self):
        violations = self.run_on_files({"preview/new.html": "<style>.x{color:#FFFFFF}</style>"})
        self.assertTrue(any(v.code == "raw-hex" for v in violations))

    def test_legacy_workspace_name_is_reported(self):
        violations = self.run_on_files({"preview/new.html": "<main><h1>Governance</h1></main>"})
        self.assertTrue(any(v.code == "legacy-workspace" for v in violations))

    def test_numeric_table_cell_requires_alignment_class(self):
        violations = self.run_on_files({"preview/new.html": "<table><tr><td>123.45</td></tr></table>"})
        self.assertTrue(any(v.code == "numeric-table-cell" for v in violations))


if __name__ == "__main__":
    unittest.main()
