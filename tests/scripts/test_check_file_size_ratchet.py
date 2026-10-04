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


@contextlib.contextmanager
def unenumerable(dir_name: str):
    """Make the named directory fail enumeration, as an EACCES during os.walk would.

    Simulated by wrapping os.walk rather than by chmod, for the same reason `unreadable` patches a
    helper: these tests may run as root, where a permission fixture silently passes. The wrapper
    reproduces what the real walk does on a scandir failure — it reports the directory to the
    onerror callback it was given (None if the caller wired none, which is the defect this fixture
    exists to catch) and yields nothing from that subtree.
    """
    original = ratchet.os.walk

    def failing(top, topdown=True, onerror=None, followlinks=False):
        for entry in original(top, topdown=topdown, onerror=onerror, followlinks=followlinks):
            dirpath = Path(entry[0])
            if dir_name in dirpath.parts:
                if dirpath.name == dir_name and onerror is not None:
                    onerror(PermissionError(13, "Permission denied", str(dirpath)))
                continue
            yield entry

    ratchet.os.walk = failing
    try:
        yield
    finally:
        ratchet.os.walk = original


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

    # The same fail-closed rule covers a directory the walk could not enumerate: every file under
    # it is invisible, which is worse than one unreadable file, not better.
    def test_refuses_to_update_the_baseline_when_a_directory_cannot_be_enumerated(self):
        with fake_repo({"src/ok.cs": 40, "src/vault/hidden.cs": 30},
                       {"src/ok.cs": 40, "src/vault/hidden.cs": 30}) as root:
            with unenumerable("vault"):
                code, output = run(["--threshold", "10", "--update-baseline"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to rewrite the baseline", output)
            self.assertIn("src/vault", output)
            self.assertEqual(read_baseline(root), {"src/ok.cs": 40, "src/vault/hidden.cs": 30})

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


def read_payload(root: Path) -> dict:
    return json.loads((root / "build" / "config" / "file-size-baseline.json").read_text())


class TightenBaselineTests(unittest.TestCase):
    """--tighten-baseline against the numbered defects of #2675's withdrawn first attempt.

    All fixtures use threshold 10 (written into the baseline by fake_repo); tighten reads the
    threshold from the baseline rather than the command line, so none of these pass --threshold.
    """

    def test_never_raises_a_cap_and_reports_actual_headroom(self):
        # Requirements 1 and 7. 29 lines with a buffer of 50 targets a cap of 79, but the old cap
        # is 30 - so the cap stays 30, and the headroom reported is the 1 line actually retained,
        # not the 50 requested.
        with fake_repo({"src/a.cs": 29}, {"src/a.cs": 30}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "50"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 30})
            self.assertIn("retained 1 line(s) of working headroom (requested 50 per file)", output)

    def test_lowers_a_cap_to_lines_plus_buffer(self):
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 25})
            self.assertEqual(read_payload(root)["headroom"], {"src/a.cs": 5})

    # Defect 1: a retired entry's reduction vanished from the progress figure at exactly the
    # moment a decomposition achieved the headline goal.
    def test_counts_a_retired_entry_in_the_locked_in_figure(self):
        # 5 lines + buffer 5 = 10 <= threshold 10, so the entry retires. Its future effective cap
        # is the threshold, so the locked-in reduction is 30 - 10 = 20.
        with fake_repo({"src/small.cs": 5}, {"src/small.cs": 30}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {})
            self.assertIn("1 retired", output)
            self.assertIn("Locked in 20 capped line(s) (20 from retired entries)", output)

    # Defect 2: a file one line under the threshold was retired by the buffer, so its next added
    # line failed as a brand-new god file - a harder failure than the cap it just lost.
    def test_keeps_an_entry_until_the_threshold_supplies_the_headroom(self):
        # 9 lines + buffer 5 = 14 > threshold 10: the threshold alone cannot supply the requested
        # headroom, so the entry is kept, capped at 14.
        with fake_repo({"src/edge.cs": 9}, {"src/edge.cs": 21}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/edge.cs": 14})
            self.assertIn("0 retired", output)

    # Defect 3: an unreadable tracked file counted as empty and had its cap written away.
    def test_refuses_when_a_tracked_file_is_unreadable(self):
        with fake_repo({"src/locked.cs": 8}, {"src/locked.cs": 30}) as root:
            with unreadable("locked.cs"):
                code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to tighten", output)
            self.assertIn("src/locked.cs", output)
            self.assertEqual(read_baseline(root), {"src/locked.cs": 30})

    # Defect 4: an unreadable *untracked* source was silently skipped, so tightening could
    # rewrite the baseline while missing a new god file entirely.
    def test_refuses_when_an_untracked_source_is_unreadable(self):
        with fake_repo({"src/ok.cs": 20, "src/mystery.cs": 40}, {"src/ok.cs": 30}) as root:
            with unreadable("mystery.cs"):
                code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to tighten", output)
            self.assertIn("src/mystery.cs", output)
            self.assertEqual(read_baseline(root), {"src/ok.cs": 30})

    # Defect 5: tightening wrote the baseline and returned 0 while the ratchet was failing.
    def test_refuses_while_the_ratchet_is_failing(self):
        with fake_repo({"src/grown.cs": 40, "src/fresh.cs": 50},
                       {"src/grown.cs": 30}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 1)
            self.assertIn("refusing to tighten while the ratchet is failing", output)
            self.assertIn("NEW god file: src/fresh.cs", output)
            self.assertIn("GREW past cap: src/grown.cs", output)
            self.assertEqual(read_baseline(root), {"src/grown.cs": 30})

    # Defect 6: retained buffer read as reclaimable slack, so the next ordinary run recommended
    # the command that destroys the headroom the buffer just created.
    def test_retained_headroom_is_not_reported_as_reclaimable(self):
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}):
            code, output = run(["--tighten-baseline", "--buffer", "5"])
            self.assertEqual(code, 0, output)

            code, output = run(["--threshold", "10"])
            self.assertEqual(code, 0, output)
            self.assertIn("0 line(s) reclaimable", output)
            self.assertIn("5 further line(s) of cap are deliberate working headroom", output)
            self.assertNotIn("not yet locked in", output)

    def test_a_further_reduction_beyond_the_headroom_is_reclaimable_again(self):
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])
            self.assertEqual(code, 0, output)

            # The file shrinks by another 12 lines after the tightening.
            (root / "src" / "a.cs").write_text("\n".join("x" for _ in range(8)) + "\n")
            code, output = run(["--threshold", "10"])

            self.assertEqual(code, 0, output)
            self.assertIn("12 line(s) reclaimable", output)
            self.assertIn("not yet locked in", output)
            self.assertIn("--tighten-baseline", output)

    def test_a_deleted_file_retires(self):
        # Deleted and unreadable are different answers: gone really is zero lines.
        with fake_repo({"src/kept.cs": 20}, {"src/kept.cs": 30, "src/gone.cs": 40}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/kept.cs": 25})
            self.assertIn("1 retired", output)
            self.assertIn("src/gone.cs", output)

    # A deleted file used to retire only via the buffer rule, which a buffer larger than the
    # threshold fails: 0 + buffer > threshold kept the entry and recorded the whole cap of a
    # nonexistent file as deliberate working headroom.
    def test_a_deleted_file_retires_even_when_the_buffer_exceeds_the_threshold(self):
        with fake_repo({"src/kept.cs": 20}, {"src/kept.cs": 30, "src/gone.cs": 40}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "25"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/kept.cs": 30})
            self.assertIn("1 retired", output)
            self.assertIn("src/gone.cs", output)
            self.assertNotIn("src/gone.cs", read_payload(root).get("headroom", {}))

    # Zero lines alone does not prove deletion: an existing file can be empty. Retiring it under
    # an oversized buffer would hand a later rebuild the brand-new-god-file failure instead of
    # the cap the operator asked to keep.
    def test_an_emptied_file_is_not_retired_as_deleted_when_the_buffer_exceeds_the_threshold(self):
        with fake_repo({"src/kept.cs": 20}, {"src/kept.cs": 30, "src/emptied.cs": 40}) as root:
            (root / "src" / "emptied.cs").write_text("", encoding="utf-8")
            code, output = run(["--tighten-baseline", "--buffer", "25"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/kept.cs": 30, "src/emptied.cs": 25})
            self.assertIn("0 retired", output)

    # os.walk ignores scandir errors unless given an onerror callback, so an unenumerable
    # subtree used to vanish from the scan entirely — and tightening would rewrite the baseline
    # having never seen whatever that subtree holds.
    def test_refuses_to_tighten_when_a_directory_cannot_be_enumerated(self):
        with fake_repo({"src/ok.cs": 20, "src/vault/secret.cs": 40},
                       {"src/ok.cs": 30, "src/vault/secret.cs": 40}) as root:
            with unenumerable("vault"):
                code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 2)
            self.assertIn("refusing to tighten", output)
            self.assertIn("src/vault", output)
            self.assertEqual(read_baseline(root),
                             {"src/ok.cs": 30, "src/vault/secret.cs": 40})

    # An excluded subtree holds only files the ratchet never governs, so failing to enumerate it
    # hides nothing and must not veto a rewrite the way a governed subtree does.
    def test_an_unenumerable_excluded_directory_does_not_block_tightening(self):
        with fake_repo({"src/ok.cs": 20, "src/generated/big.cs": 40},
                       {"src/ok.cs": 30}) as root:
            with unenumerable("generated"):
                code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/ok.cs": 25})

    # Contract slip 1: --buffer accepted and ignored outside tightening, so
    # `--update-baseline --buffer 25` exited 0 while pinning every cap.
    def test_buffer_without_tighten_is_an_error(self):
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 30}) as root:
            code, output = run(["--update-baseline", "--buffer", "25"])

            self.assertEqual(code, 2)
            self.assertIn("--buffer is only meaningful with --tighten-baseline", output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 30})

    # Contract slip 2: an explicit --threshold retired files the baseline still protected.
    def test_tighten_with_explicit_threshold_is_an_error(self):
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 30}) as root:
            code, output = run(["--tighten-baseline", "--threshold", "50"])

            self.assertEqual(code, 2)
            self.assertIn("threshold recorded in the baseline", output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 30})

    def test_tighten_uses_the_baseline_threshold_not_the_default(self):
        # fake_repo records threshold 10 in the baseline; the module default is 2000. If tighten
        # used the default, every fixture entry would retire (lines + buffer << 2000).
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            code, output = run(["--tighten-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 25})

    def test_update_baseline_drops_recorded_headroom(self):
        # Re-pinning every cap at its exact size leaves nothing deliberate about later slack.
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            run(["--tighten-baseline", "--buffer", "5"])
            self.assertIn("headroom", read_payload(root))

            code, output = run(["--threshold", "10", "--update-baseline"])

            self.assertEqual(code, 0, output)
            self.assertNotIn("headroom", read_payload(root))


class RelaxBaselineTests(unittest.TestCase):
    """--relax-baseline: grants working headroom without becoming a way to absorb growth.

    Fixtures use threshold 10 (written into the baseline by fake_repo); relax reads the threshold
    from the baseline, so none of these pass --threshold.
    """

    def test_raises_a_cap_pinned_at_the_files_current_size(self):
        # The state --update-baseline leaves behind, and the one this command exists for: cap
        # equals size, so a single added line fails CI.
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}) as root:
            code, output = run(["--relax-baseline", "--buffer", "50"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 80})
            self.assertEqual(read_payload(root)["headroom"], {"src/a.cs": 50})
            self.assertIn("1 cap(s) raised", output)

    def test_never_lowers_a_cap(self):
        # 20 lines + buffer 5 targets 25, but the cap is already 90. Lowering is the tightening
        # direction; doing it here would destroy a deliberate reduction.
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            code, output = run(["--relax-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 90})

    def test_records_only_the_buffer_as_deliberate_headroom(self):
        # The cap leaves 70 lines spare, but only the 5-line buffer is deliberate. Recording all
        # 70 would erase a real reduction from the reclaimable figure.
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            code, output = run(["--relax-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_payload(root)["headroom"], {"src/a.cs": 5})
            self.assertIn("65 line(s) reclaimable", output)

    def test_does_not_reduce_headroom_a_tightening_recorded(self):
        # Tighten to 20+15=35 with 15 recorded, then relax with a smaller buffer. The smaller
        # buffer must not quietly retract slack an earlier run chose on purpose.
        with fake_repo({"src/a.cs": 20}, {"src/a.cs": 90}) as root:
            run(["--tighten-baseline", "--buffer", "15"])
            self.assertEqual(read_payload(root)["headroom"], {"src/a.cs": 15})

            code, output = run(["--relax-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertEqual(read_baseline(root), {"src/a.cs": 35})
            self.assertEqual(read_payload(root)["headroom"], {"src/a.cs": 15})

    def test_clears_the_tight_warning(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}) as root:
            _, before = run([])
            self.assertIn("TIGHT", before)

            run(["--relax-baseline", "--buffer", "50"])
            code, after = run([])

            self.assertEqual(code, 0, after)
            self.assertNotIn("TIGHT", after)

    def test_a_later_edit_within_the_buffer_passes(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}) as root:
            run(["--relax-baseline", "--buffer", "50"])
            (root / "src" / "a.cs").write_text("\n".join("x" for _ in range(45)) + "\n")

            code, output = run([])

            self.assertEqual(code, 0, output)

    def test_refuses_while_the_ratchet_is_failing(self):
        # The abuse this guard exists for: relaxing a grown file would wave it through.
        with fake_repo({"src/a.cs": 40}, {"src/a.cs": 30}):
            code, output = run(["--relax-baseline"])

            self.assertEqual(code, 1, output)
            self.assertIn("refusing to relax while the ratchet is failing", output)
            self.assertIn("GREW past cap", output)

    def test_refuses_when_an_untracked_source_is_unreadable(self):
        # An unreadable governed source is invisible to the scan, so a new god file could ride
        # through the very command that rewrites the protections.
        with fake_repo({"src/a.cs": 30, "src/mystery.cs": 30}, {"src/a.cs": 30}):
            with unreadable("mystery.cs"):
                code, output = run(["--relax-baseline"])

            self.assertEqual(code, 2, output)
            self.assertIn("refusing to relax", output)

    def test_refuses_when_a_tracked_file_is_unreadable(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30, "src/gone.cs": 40}):
            with unreadable("gone.cs"):
                code, output = run(["--relax-baseline"])

            self.assertEqual(code, 2, output)
            self.assertIn("refusing to relax", output)

    def test_does_not_retire_an_entry(self):
        # 3 lines + buffer 5 is under the threshold, but dropping protections is the tightening
        # direction; a command that grants headroom must not also retire caps.
        with fake_repo({"src/small.cs": 3}, {"src/small.cs": 30}) as root:
            code, output = run(["--relax-baseline", "--buffer", "5"])

            self.assertEqual(code, 0, output)
            self.assertIn("src/small.cs", read_baseline(root))

    def test_buffer_is_accepted_with_relax(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}):
            code, output = run(["--relax-baseline", "--buffer", "10"])

            self.assertEqual(code, 0, output)

    def test_relax_and_tighten_are_mutually_exclusive(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}):
            code, output = run(["--relax-baseline", "--tighten-baseline"])

            self.assertEqual(code, 2, output)
            self.assertIn("mutually exclusive", output)

    def test_relax_rejects_an_explicit_threshold(self):
        with fake_repo({"src/a.cs": 30}, {"src/a.cs": 30}):
            code, output = run(["--relax-baseline", "--threshold", "10"])

            self.assertEqual(code, 2, output)
            self.assertIn("uses the threshold recorded in the baseline", output)


if __name__ == "__main__":
    unittest.main()
