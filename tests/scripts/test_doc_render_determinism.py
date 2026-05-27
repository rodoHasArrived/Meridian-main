import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIX = ROOT / "tests" / "scripts" / "fixtures" / "roadmap"


class DocRenderDeterminismTests(unittest.TestCase):
    def test_rejects_ambiguous_datetime(self):
        out = ROOT / "artifacts" / "tmp-ambiguous.yaml"
        cmd = [
            "python3",
            str(ROOT / "tools" / "roadmap" / "render_roadmap_docs.py"),
            str(FIX / "ambiguous-input.yaml"),
            str(out),
            "--date-field",
            "release_date",
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("YYYY-MM-DD", result.stderr + result.stdout)

    def test_normalizes_unicode_and_sorts_keys(self):
        with tempfile.TemporaryDirectory() as td:
            out = Path(td) / "normalized.yaml"
            cmd = [
                "python3",
                str(ROOT / "tools" / "roadmap" / "render_roadmap_docs.py"),
                str(FIX / "unordered-mixed-unicode.yaml"),
                str(out),
                "--date-field",
                "release_date",
            ]
            subprocess.run(cmd, check=True)
            text = out.read_text(encoding="utf-8")
            self.assertNotIn("Cafe\u0301", text)
            self.assertIn("Café", text)
            self.assertLess(text.index("a_key"), text.index("z_key"))


if __name__ == "__main__":
    unittest.main()
