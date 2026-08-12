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


class TightenBaselineTests(unittest.TestCase):
    def test_lowers_a_cap_when_the_file_shrank(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 15)
            self.assertIn("15 line(s) reclaimed", output)

    # The safety property the whole flag rests on: --tighten-baseline must never be able to record
    # growth, which is what separates it from --update-baseline. A grown file is refused outright
    # rather than quietly written past, so tightening cannot hand automation a success for a tree
    # that fails the ratchet.
    def test_refuses_to_tighten_while_a_file_has_grown(self):
        with fake_repo({"src/big.cs": 40}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to tighten while the ratchet is failing", output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 30)

    def test_refuses_to_tighten_while_a_new_god_file_exists(self):
        with fake_repo({"src/big.cs": 20, "src/fresh.cs": 40}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("NEW god file", output)
            # The baseline is left exactly as it was.
            self.assertEqual(read_baseline(root), {"src/big.cs": 30})

    def test_retires_a_file_that_dropped_below_the_threshold(self):
        with fake_repo({"src/small.cs": 5}, {"src/small.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline"])

            self.assertEqual(code, 0, output)
            self.assertNotIn("src/small.cs", read_baseline(root))
            self.assertIn("retired", output)

    # Retiring a file is the plan's headline goal, so its reduction must show up in the total
    # rather than being dropped along with its baseline entry.
    def test_counts_a_retired_file_in_the_reclaimed_total(self):
        with fake_repo({"src/small.cs": 5}, {"src/small.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline"])

            self.assertEqual(code, 0, output)
            self.assertIn("25 line(s) reclaimed", output)
            self.assertNotIn("src/small.cs", read_baseline(root))

    # Retiring drops the cap, leaving only the threshold as protection. A file parked just under
    # the threshold would then fail as a brand-new god file on the next line, not use its buffer.
    def test_holds_an_entry_until_the_threshold_supplies_the_buffer(self):
        with fake_repo({"src/near.cs": 9}, {"src/near.cs": 40}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root)["src/near.cs"], 14)
            self.assertIn("0 file(s) retired", output)

    def test_retires_once_the_threshold_itself_covers_the_buffer(self):
        with fake_repo({"src/tiny.cs": 3}, {"src/tiny.cs": 40}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertNotIn("src/tiny.cs", read_baseline(root))
            self.assertIn("1 file(s) retired", output)

    def test_buffer_keeps_working_headroom_while_still_reclaiming(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 20)
            self.assertIn("10 line(s) reclaimed", output)
            self.assertIn("5 line(s) of headroom kept", output)

    # The buffer must not become a way to raise a cap on a file that has not shrunk enough.
    def test_buffer_never_raises_a_cap(self):
        with fake_repo({"src/big.cs": 29}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "50"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 30)

    # Claiming the requested buffer was kept when the existing cap allowed only one line would
    # send a contributor into the next ratchet failure believing they had room.
    def test_reports_the_headroom_actually_retained_not_the_request(self):
        with fake_repo({"src/big.cs": 29}, {"src/big.cs": 30}):
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "25"])

            self.assertEqual(code, 0, output)
            self.assertIn("as little as 1 line(s) of headroom retained", output)
            self.assertIn("requested 25", output)

    def test_rejects_a_negative_buffer(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}):
            code, output = run(["--threshold", "10", "--tighten-baseline", "--buffer", "-1"])

            self.assertEqual(code, 2)
            self.assertIn("cannot be negative", output)

    # Raising the threshold makes _scan omit files the baseline still protects; retiring them would
    # be a downward-only operation quietly deleting caps.
    def test_rejects_a_threshold_that_differs_from_the_baseline(self):
        with fake_repo({"src/big.cs": 30}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "40", "--tighten-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("requires the baseline's own threshold", output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 30)

    def test_rejects_being_combined_with_update_baseline(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}):
            code, output = run(["--threshold", "10", "--update-baseline", "--tighten-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("choose either", output)

    # A grown file must still fail the check afterwards - tightening is not an escape hatch.
    def test_tightening_does_not_excuse_a_grown_file(self):
        with fake_repo({"src/big.cs": 40}, {"src/big.cs": 30}):
            run(["--threshold", "10", "--tighten-baseline"])
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 1)
            self.assertIn("GREW past cap", output)

    # An unreadable tracked file counts as zero under the tolerant reader, which would retire it
    # and write its cap away. A mutating run must abort instead.
    def test_refuses_to_tighten_when_a_tracked_file_cannot_be_read(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}) as root:
            unreadable = root / "src" / "big.cs"
            unreadable.chmod(0o000)
            try:
                code, output = run(["--threshold", "10", "--tighten-baseline"])
            finally:
                unreadable.chmod(0o644)

            # Running as root defeats the permission bit; skip rather than assert a false pass.
            if code == 0:
                self.skipTest("filesystem permissions are not enforced for this user")
            self.assertEqual(code, 2)
            self.assertIn("cannot read a tracked file", output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 30)

    def test_rejects_buffer_without_tightening(self):
        with fake_repo({"src/big.cs": 15}, {"src/big.cs": 30}) as root:
            code, output = run(["--threshold", "10", "--update-baseline", "--buffer", "25"])

            self.assertEqual(code, 2)
            self.assertIn("--buffer applies only to --tighten-baseline", output)
            self.assertEqual(read_baseline(root)["src/big.cs"], 30)


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
