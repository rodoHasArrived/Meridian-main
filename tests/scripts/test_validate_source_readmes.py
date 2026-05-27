from __future__ import annotations

import subprocess
import tempfile
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

    def test_absolute_readme_path_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            modules = temp_root / "modules.yml"
            coverage = temp_root / "coverage.yml"
            outside_readme = temp_root / "outside.md"
            outside_readme.write_text("# outside", encoding="utf-8")
            modules.write_text("modules: []\n", encoding="utf-8")
            coverage.write_text(
                "readme_coverage:\n"
                "  modules:\n"
                "    - id: attack.module\n"
                f"      readme_path: {outside_readme}\n",
                encoding="utf-8",
            )

            result = subprocess.run(
                [
                    "python3",
                    str(self.script),
                    "--modules",
                    str(modules),
                    "--coverage",
                    str(coverage),
                    "--repo-root",
                    str(self.repo_root),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn("README path must be relative to repository root", result.stdout + result.stderr)

    def test_symlink_readme_path_escaping_repo_root_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            modules = temp_root / "modules.yml"
            coverage = temp_root / "coverage.yml"
            outside_readme = temp_root / "outside.md"
            outside_readme.write_text("# outside", encoding="utf-8")
            symlink_path = self.repo_root / "tools/source_docs/fixtures/readme_contract/symlink-escape.md"
            try:
                if symlink_path.exists() or symlink_path.is_symlink():
                    symlink_path.unlink()
                symlink_path.symlink_to(outside_readme)
                modules.write_text("modules: []\n", encoding="utf-8")
                coverage.write_text(
                    "readme_coverage:\n"
                    "  modules:\n"
                    "    - id: attack.module\n"
                    f"      readme_path: {symlink_path.relative_to(self.repo_root)}\n",
                    encoding="utf-8",
                )

                result = subprocess.run(
                    [
                        "python3",
                        str(self.script),
                        "--modules",
                        str(modules),
                        "--coverage",
                        str(coverage),
                        "--repo-root",
                        str(self.repo_root),
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )

                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertIn("README path escapes repository root", result.stdout + result.stderr)
            finally:
                if symlink_path.exists() or symlink_path.is_symlink():
                    symlink_path.unlink()


if __name__ == "__main__":
    unittest.main()
