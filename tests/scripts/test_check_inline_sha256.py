from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-inline-sha256.py"
)
SPEC = importlib.util.spec_from_file_location("check_inline_sha256", SCRIPT_PATH)
assert SPEC and SPEC.loader
ratchet = importlib.util.module_from_spec(SPEC)
sys.modules["check_inline_sha256"] = ratchet
SPEC.loader.exec_module(ratchet)


def count(files: dict[str, str]) -> dict[str, int]:
    """Run the counter over a throwaway tree holding exactly these src/ files."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        for rel, content in files.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        return ratchet.count_call_sites(root)


class CheckInlineSha256Tests(unittest.TestCase):
    def test_counts_inline_call_sites_per_file(self) -> None:
        counts = count(
            {
                "src/Meridian.Sample/One.cs": "var a = SHA256.HashData(x); var b = SHA256.HashData(y);",
                "src/Meridian.Sample/Two.cs": "return Convert.ToHexString(SHA256.HashData(z));",
            }
        )

        self.assertEqual(
            counts,
            {
                "src/Meridian.Sample/One.cs": 2,
                "src/Meridian.Sample/Two.cs": 1,
            },
        )

    def test_canonical_home_is_excluded(self) -> None:
        counts = count(
            {
                "src/Meridian.Contracts/Integrity/Sha256Digest.cs": "SHA256.HashData(value);",
                "src/Meridian.Sample/Elsewhere.cs": "SHA256.HashData(value);",
            }
        )

        self.assertEqual(counts, {"src/Meridian.Sample/Elsewhere.cs": 1})

    def test_files_without_inline_hashing_are_not_reported(self) -> None:
        counts = count(
            {
                "src/Meridian.Sample/Clean.cs": "var hash = Sha256Digest.Compute(bytes);",
            }
        )

        self.assertEqual(counts, {})

    def test_build_artifacts_are_skipped(self) -> None:
        counts = count(
            {
                "src/Meridian.Sample/obj/Generated.cs": "SHA256.HashData(value);",
                "src/Meridian.Sample/bin/Generated.cs": "SHA256.HashData(value);",
            }
        )

        self.assertEqual(counts, {})

    def test_repository_baseline_is_satisfied(self) -> None:
        """The checked-in baseline must match or exceed the current tree, so the ratchet
        never fails a fresh checkout."""
        import json

        repo_root = Path(__file__).resolve().parents[2]
        current = ratchet.count_call_sites(repo_root)
        baseline = json.loads(
            (repo_root / "build" / "scripts" / "ci" / "inline-sha256-baseline.json").read_text(
                encoding="utf-8"
            )
        )["files"]

        for rel, observed in current.items():
            self.assertIn(rel, baseline, f"unbaselined inline SHA-256 site: {rel}")
            self.assertLessEqual(
                observed,
                baseline[rel],
                f"{rel} exceeds its baseline count",
            )


if __name__ == "__main__":
    unittest.main()
