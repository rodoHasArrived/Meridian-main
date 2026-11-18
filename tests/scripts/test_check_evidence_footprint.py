"""Tests for the no-new-raw-evidence ratchet."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "check_evidence_footprint", REPO / "build/scripts/ci/check-evidence-footprint.py"
)
assert SPEC is not None and SPEC.loader is not None
ratchet = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ratchet)


class EvidenceFootprintTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        # A plain directory (not a git checkout) exercises the walk fallback.
        self.root = Path(self.temporary.name)

    def put(self, rel: str, size: int) -> None:
        path = self.root / ratchet.EVIDENCE_ROOT / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"x" * size)

    def run_main(self, *args: str) -> int:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return ratchet.main(["--root", str(self.root), *args])

    def baseline(self) -> dict[str, int]:
        return json.loads((self.root / ratchet.BASELINE).read_text(encoding="utf-8"))["files"]

    def test_classifies_raw_by_suffix_or_size(self) -> None:
        threshold = ratchet.THRESHOLD_BYTES
        self.assertTrue(ratchet.is_raw("a/run.LOG", 10, threshold))
        self.assertTrue(ratchet.is_raw("a/out.json.gz", 10, threshold))
        self.assertTrue(ratchet.is_raw("a/requests.json", threshold + 1, threshold))
        self.assertFalse(ratchet.is_raw("a/README.md", threshold, threshold))

    def test_new_raw_file_fails_and_summary_files_pass(self) -> None:
        self.put("run-1/README.md", 100)
        self.assertEqual(self.run_main("--update-baseline"), 0)
        self.assertEqual(self.baseline(), {})
        self.put("run-2/README.md", 100)
        self.assertEqual(self.run_main(), 0)
        self.put("run-2/build.log", 10)
        self.assertEqual(self.run_main(), 1)

    def test_baselined_file_may_shrink_but_not_grow(self) -> None:
        self.put("run-1/build.log", 50)
        self.assertEqual(self.run_main("--update-baseline"), 0)
        self.put("run-1/build.log", 40)
        self.assertEqual(self.run_main(), 0)
        self.put("run-1/build.log", 60)
        self.assertEqual(self.run_main(), 1)

    def test_tighten_only_lowers_and_retires(self) -> None:
        self.put("run-1/a.log", 50)
        self.put("run-1/b.log", 50)
        self.assertEqual(self.run_main("--update-baseline"), 0)
        (self.root / ratchet.EVIDENCE_ROOT / "run-1/a.log").unlink()
        self.put("run-1/b.log", 20)
        self.assertEqual(self.run_main("--tighten-baseline"), 0)
        self.assertEqual(self.baseline(), {f"{ratchet.EVIDENCE_ROOT}/run-1/b.log": 20})

    def test_tighten_refuses_while_failing(self) -> None:
        self.assertEqual(self.run_main("--update-baseline"), 0)
        self.put("run-1/new.log", 5)
        self.assertEqual(self.run_main("--tighten-baseline"), 1)

    def test_repository_tree_passes(self) -> None:
        threshold, baseline = ratchet.load_baseline(REPO)
        violations, _ = ratchet.evaluate(ratchet.current_raw(REPO, threshold), baseline)
        self.assertEqual(violations, [])


if __name__ == "__main__":
    unittest.main()
