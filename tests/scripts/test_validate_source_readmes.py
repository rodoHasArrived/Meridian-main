from __future__ import annotations

import subprocess
import unittest
from pathlib import Path


class ValidateSourceReadmesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.repo_root = Path(__file__).resolve().parents[2]
        self.script = self.repo_root / "tools/source_docs/validate_source_readmes.py"
        self.fixture_root = self.repo_root / "tools/source_docs/fixtures/readme_contract"
        self.modules = self.fixture_root / "modules.yml"

    def run_validator(self, coverage_file: str) -> subprocess.CompletedProcess[str]:
        coverage = self.fixture_root / coverage_file
        return subprocess.run(
            [
                "python3",
                str(self.script),
                "--modules",
                str(self.modules),
                "--coverage",
                str(coverage),
                "--repo-root",
                str(self.repo_root),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

    def test_valid_fixture_passes(self) -> None:
        result = self.run_validator("coverage-valid.yml")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Source documentation validation passed.", result.stdout)

    def test_invalid_fixture_reports_precise_contract_failures(self) -> None:
        result = self.run_validator("coverage-invalid.yml")
        output = result.stdout + result.stderr
        self.assertEqual(result.returncode, 1, output)

        self.assertIn("module=missing-front-matter-module", output)
        self.assertIn("path=tools/source_docs/fixtures/readme_contract/missing_front_matter/README.md", output)
        self.assertIn("missing=front matter block is missing", output)

        self.assertIn("module=missing-heading-module", output)
        self.assertIn("missing=missing required heading '## Dependencies and Integrations'", output)

        self.assertIn("module=missing-markers-module", output)
        self.assertIn("missing=missing generated block begin marker", output)
        self.assertIn("missing=missing generated block end marker", output)


if __name__ == "__main__":
    unittest.main()
