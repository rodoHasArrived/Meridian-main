from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "ci.yml"


class CiWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def _job_block(self, job_name: str) -> str:
        start = self.workflow.index(f"  {job_name}:")
        next_job = self.workflow.find("\n  ", start + 3)
        while next_job != -1 and self.workflow[next_job + 3 : next_job + 4].isspace():
            next_job = self.workflow.find("\n  ", next_job + 1)
        return self.workflow[start : next_job if next_job != -1 else None]

    def test_legacy_evidence_jobs_do_not_duplicate_normal_pr_quality_gate(self) -> None:
        for job_name in ("dotnet", "browser-workstation", "source-doc-determinism"):
            with self.subTest(job=job_name):
                block = self._job_block(job_name)
                self.assertIn("if: github.event_name != 'pull_request'", block)

    def test_secret_scan_remains_pull_request_visible(self) -> None:
        secret_block = self._job_block("secret-scan")

        self.assertNotIn("if: github.event_name != 'pull_request'", secret_block)
        self.assertIn("gitleaks/gitleaks-action@v3", secret_block)
        self.assertIn('GITLEAKS_VERSION: "8.25.1"', secret_block)


if __name__ == "__main__":
    unittest.main()
