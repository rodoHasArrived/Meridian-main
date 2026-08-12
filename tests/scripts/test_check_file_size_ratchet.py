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

    # A deleted file's lines really are gone, so zero is the honest count.
    def test_counts_a_deleted_file_as_zero(self):
        with fake_repo({"src/kept.cs": 20}, {"src/kept.cs": 30, "src/gone.cs": 40}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("20 current line(s)", output)
            self.assertIn("50 line(s) reclaimable", output)

    # An unreadable file read as zero is indistinguishable from a deleted one, which would report a
    # failed read as the largest possible reduction. Hold it at its cap and say so.
    #
    # The unreadable path is a directory rather than a chmod'ed file on purpose: root ignores mode
    # bits, so a permission-based fixture passes vacuously wherever tests run as root and only
    # exercises the branch on the CI runner. A directory raises OSError from open() for everyone.
    def test_does_not_report_an_unreadable_file_as_reclaimed(self):
        with fake_repo({}, {"src/locked.cs": 30}) as root:
            (root / "src" / "locked.cs").mkdir(parents=True)

            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("30 current line(s)", output)
            self.assertIn("0 line(s) reclaimable", output)
            self.assertIn("could not be read", output)
            self.assertIn("src/locked.cs", output)
            # Substituting the cap keeps the totals honest, but it is not a measurement, so the
            # file must not then be announced as sitting at its cap.
            self.assertNotIn("TIGHT", output)
            self.assertNotIn("a single added line fails this check", output)

    # The cap substitution reads as zero headroom, so an unreadable file would both claim to be
    # pinned and outrank genuinely near-cap files for the ten displayed slots.
    def test_an_unreadable_file_does_not_displace_a_genuine_tight_warning(self):
        with fake_repo({"src/near.cs": 28}, {"src/near.cs": 30, "src/locked.cs": 40}) as root:
            (root / "src" / "locked.cs").mkdir(parents=True)

            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("TIGHT: 1 of 2", output)
            self.assertIn("src/near.cs: 28/30 (2 line(s) spare)", output)
            self.assertNotIn("src/locked.cs: 40/40", output)
            self.assertNotIn("a single added line fails this check", output)

    # The trend prints after the verdict, so anything that raises here replaces a declared exit code
    # with a traceback. An untraversable parent is the realistic trigger: Path.exists() re-raises
    # EACCES rather than returning False, so probing before opening would crash the whole check.
    def test_survives_a_baselined_path_that_cannot_be_stat_ed(self):
        with fake_repo({"src/ok.cs": 20}, {"src/ok.cs": 30, "src/vault/secret.cs": 40}) as root:
            locked = root / "src" / "vault"
            locked.mkdir(parents=True)
            (locked / "secret.cs").write_text("x\n" * 40, encoding="utf-8")
            locked.chmod(0o000)
            try:
                code, output = run(["--threshold", "10"])
            finally:
                locked.chmod(0o755)

            # Root ignores mode bits, so this asserts only that the run completes with a declared
            # exit code and no traceback - true whether or not the read was actually denied.
            self.assertEqual(code, 0, output)
            self.assertIn("Baseline trend:", output)
            self.assertNotIn("Traceback", output)

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

    # The trend also prints on failure, where at least one file is over its cap by construction. A
    # negative headroom trivially satisfies "within 25 lines", so an unbounded comparison would
    # describe a file far past its cap as near it, and sort it ahead of the files that are.
    def test_does_not_list_an_over_cap_file_as_tight(self):
        with fake_repo({"src/grown.cs": 70, "src/near.cs": 28},
                       {"src/grown.cs": 30, "src/near.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("exceeded by 40", output)
            self.assertNotIn("-40 line(s) spare", output)
            self.assertIn("TIGHT: 1 of 2", output)
            self.assertIn("src/near.cs: 28/30 (2 line(s) spare)", output)
            self.assertNotIn("a single added line fails this check", output)

    def test_reports_the_trend_on_failure_too(self):
        with fake_repo({"src/grown.cs": 40}, {"src/grown.cs": 30}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("exceeded by 10", output)
            self.assertIn("Baseline trend:", output)

    # --update-baseline is the command run immediately after a decomposition, so it is the one run
    # where the trend matters most. It used to return before reporting.
    def test_reports_the_trend_after_updating_the_baseline(self):
        with fake_repo({"src/a.cs": 20, "src/b.cs": 25}, {"src/a.cs": 30, "src/b.cs": 25}) as root:
            code, output = run(["--threshold", "10", "--update-baseline"])

            self.assertEqual(code, 0, output)
            self.assertIn("Wrote baseline with 2 tracked file(s)", output)
            self.assertIn("Baseline trend:", output)
            # Freshly written caps equal current lines, so nothing is reclaimable and every file
            # is pinned - the report should say so rather than repeat the pre-update numbers.
            self.assertIn("45 capped line(s)", output)
            self.assertIn("0 line(s) reclaimable", output)
            self.assertIn("2 of them sit at their cap", output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 20, "src/b.cs": 25})

    def test_a_brand_new_oversized_file_still_fails(self):
        with fake_repo({"src/fresh.cs": 40}, {}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("NEW god file", output)


if __name__ == "__main__":
    unittest.main()
