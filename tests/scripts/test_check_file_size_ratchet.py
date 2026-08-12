from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-file-size.py"
SPEC = importlib.util.spec_from_file_location("check_file_size", SCRIPT_PATH)
assert SPEC and SPEC.loader
ratchet = importlib.util.module_from_spec(SPEC)
sys.modules["check_file_size"] = ratchet
SPEC.loader.exec_module(ratchet)


@contextlib.contextmanager
def fake_repo(sources: dict[str, int], baseline: dict[str, int] | None, threshold: int = 10):
    """Build a throwaway repo whose source files have the requested line counts."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        for rel, lines in sources.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("\n".join("x" for _ in range(lines)) + "\n", encoding="utf-8")
        if baseline is not None:
            config = root / "build" / "config"
            config.mkdir(parents=True, exist_ok=True)
            (config / "file-size-baseline.json").write_text(
                json.dumps({"threshold_lines": threshold, "files": baseline}, indent=2) + "\n",
                encoding="utf-8",
            )

        original = ratchet._repo_root
        ratchet._repo_root = lambda: root  # type: ignore[assignment]
        try:
            yield root
        finally:
            ratchet._repo_root = original  # type: ignore[assignment]


def run(argv: list[str]) -> tuple[int, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = ratchet.main(argv)
    return code, out.getvalue() + err.getvalue()


def read_baseline(root: Path) -> dict[str, int]:
    payload = json.loads((root / "build" / "config" / "file-size-baseline.json").read_text())
    return {str(k): int(v) for k, v in payload["files"].items()}



class TrendReportingTests(unittest.TestCase):
    def test_reports_tracked_totals_and_reclaimable_lines(self):
        with fake_repo({"src/a.cs": 20, "src/b.cs": 25}, {"src/a.cs": 30, "src/b.cs": 25}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("2 tracked file(s)", output)
            self.assertIn("55 capped line(s)", output)
            self.assertIn("45 current line(s)", output)
            self.assertIn("10 line(s) reclaimable", output)

    # A baselined file that shrank below the threshold drops out of the scan. Its lines still exist,
    # so counting it as zero would erase them from the totals and overstate what is reclaimable.
    def test_counts_a_file_that_shrank_below_the_threshold(self):
        with fake_repo({"src/shrunk.cs": 8}, {"src/shrunk.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("8 current line(s)", output)
            self.assertIn("22 line(s) reclaimable", output)

    def test_warns_about_files_sitting_at_their_cap(self):
        with fake_repo({"src/pinned.cs": 30}, {"src/pinned.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("TIGHT", output)
            self.assertIn("0 line(s) spare", output)
            self.assertIn("a single added line fails this check", output)

    # A file with spare lines is approaching its cap, but one more line still passes - the warning
    # must not claim otherwise.
    def test_does_not_claim_one_line_fails_when_headroom_remains(self):
        with fake_repo({"src/near.cs": 25}, {"src/near.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("TIGHT", output)
            self.assertIn("5 line(s) spare", output)
            self.assertNotIn("a single added line fails this check", output)

    def test_does_not_warn_when_there_is_headroom(self):
        with fake_repo({"src/roomy.cs": 20}, {"src/roomy.cs": 500}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertNotIn("TIGHT", output)

    def test_reports_the_trend_on_failure_too(self):
        with fake_repo({"src/grown.cs": 40}, {"src/grown.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("exceeded by 10", output)
            self.assertIn("Baseline trend:", output)

    def test_a_brand_new_oversized_file_still_fails(self):
        with fake_repo({"src/fresh.cs": 40}, {}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("NEW god file", output)


if __name__ == "__main__":
    unittest.main()
