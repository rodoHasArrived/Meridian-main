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


@contextlib.contextmanager
def unreadable(*file_names: str):
    """Make the named files report an unknown size, as a permission or I/O error would.

    Patched rather than staged on disk: these tests run as root in some environments, where chmod is
    ignored and a permission fixture would pass without exercising anything. `_scan` also only walks
    real filenames, so substituting a directory - which works for the baselined-path reader - never
    reaches it. Patching the one helper both readers share is the only form that holds everywhere.
    """
    original = ratchet._try_count_lines
    targets = set(file_names)

    def failing(path):
        return None if path.name in targets else original(path)

    ratchet._try_count_lines = failing
    try:
        yield
    finally:
        ratchet._try_count_lines = original


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
    def test_does_not_report_an_unreadable_file_as_reclaimed(self):
        with fake_repo({"src/locked.cs": 8}, {"src/locked.cs": 30}):
            with unreadable("locked.cs"):
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
        with fake_repo({"src/near.cs": 28, "src/locked.cs": 40},
                       {"src/near.cs": 30, "src/locked.cs": 40}):
            with unreadable("locked.cs"):
                code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("TIGHT: 1 of 2", output)
            self.assertIn("src/near.cs: 28/30 (2 line(s) spare)", output)
            self.assertNotIn("src/locked.cs: 40/40", output)
            self.assertNotIn("a single added line fails this check", output)

    # The trend prints after the verdict, so anything that raises here replaces a declared exit code
    # with a traceback. The realistic trigger is an untraversable parent, where Path.exists() and
    # Path.is_file() re-raise EACCES rather than returning False - which is why neither is used.
    def test_survives_a_baselined_path_that_cannot_be_read(self):
        with fake_repo({"src/ok.cs": 20, "src/vault/secret.cs": 40},
                       {"src/ok.cs": 30, "src/vault/secret.cs": 40}):
            with unreadable("secret.cs"):
                code, output = run(["--threshold", "10"])

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

    # Absent from the scan is how this script spells "no longer oversized", so writing the baseline
    # from a scan that could not read everything silently retires the caps it could not see - and
    # the trend printed afterwards reloads the new baseline, so the dropped entry is invisible.
    def test_refuses_to_update_the_baseline_when_a_source_cannot_be_read(self):
        with fake_repo({"src/ok.cs": 40, "src/flaky.cs": 30},
                       {"src/ok.cs": 40, "src/flaky.cs": 30}) as root:
            with unreadable("flaky.cs"):
                code, output = run(["--threshold", "10", "--update-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to rewrite the baseline", output)
            self.assertIn("src/flaky.cs", output)
            # The cap it could not verify must survive the refusal.
            self.assertEqual(read_baseline(root), {"src/ok.cs": 40, "src/flaky.cs": 30})

    # The read-only path keeps reporting rather than failing: a check that cannot see a file should
    # not invent a verdict about it, and a transient error must not redden an unrelated PR.
    def test_an_unreadable_source_does_not_fail_the_ordinary_check(self):
        with fake_repo({"src/ok.cs": 20, "src/flaky.cs": 30}, {"src/ok.cs": 30}):
            with unreadable("flaky.cs"):
                code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("Baseline trend:", output)

    def test_a_brand_new_oversized_file_still_fails(self):
        with fake_repo({"src/fresh.cs": 40}, {}):
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("NEW god file", output)


if __name__ == "__main__":
    unittest.main()
