from __future__ import annotations

import re
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
CI_SCRIPT = REPO_ROOT / "scripts" / "ci.sh"


def _browser_results_block() -> str:
    script = CI_SCRIPT.read_text(encoding="utf-8")
    match = re.search(r'^if \[\[ -z "\$\{MERIDIAN_BROWSER_RESULTS_DIR:-\}" \]\]; then\n.*?^fi\n',
                      script, flags=re.M | re.S)
    if match is None:
        raise AssertionError("ci.sh no longer defines the default browser results directory")
    return match.group(0)


class CiBrowserResultsDirTests(unittest.TestCase):
    """The dashboard runner resolves the results directory from the dashboard folder.

    A relative default once placed shard summaries under src/Meridian.Ui/dashboard/artifacts,
    so the hosted shard upload (repository-root artifacts/) carried no summary and the combined
    browser verdict rejected every run.
    """

    def _resolve(self, artifact_root: str, cwd: Path) -> str:
        command = (
            f'unset MERIDIAN_BROWSER_RESULTS_DIR; MERIDIAN_CI_ARTIFACT_ROOT="{artifact_root}"\n'
            + _browser_results_block()
            + 'printf "%s" "$MERIDIAN_BROWSER_RESULTS_DIR"'
        )
        return subprocess.run(["bash", "-c", command], cwd=cwd, check=True,
                              capture_output=True, text=True).stdout

    def test_relative_artifact_root_becomes_absolute_from_the_invocation_directory(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            root = Path(directory).resolve()
            self.assertEqual(self._resolve("artifacts", root), f"{root}/artifacts/test-results/browser")

    def test_absolute_artifact_root_is_kept(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            self.assertEqual(self._resolve("/tmp/meridian-ci", Path(directory)),
                             "/tmp/meridian-ci/test-results/browser")

    def test_explicit_results_directory_is_not_overridden(self) -> None:
        command = ('MERIDIAN_BROWSER_RESULTS_DIR=/explicit; MERIDIAN_CI_ARTIFACT_ROOT=artifacts\n'
                   + _browser_results_block() + 'printf "%s" "$MERIDIAN_BROWSER_RESULTS_DIR"')
        result = subprocess.run(["bash", "-c", command], check=True, capture_output=True, text=True)
        self.assertEqual(result.stdout, "/explicit")


if __name__ == "__main__":
    unittest.main()
