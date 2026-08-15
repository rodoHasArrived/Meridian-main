from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-duplicate-helpers.py"
SPEC = importlib.util.spec_from_file_location("check_duplicate_helpers", SCRIPT_PATH)
assert SPEC and SPEC.loader
ratchet = importlib.util.module_from_spec(SPEC)
sys.modules["check_duplicate_helpers"] = ratchet
SPEC.loader.exec_module(ratchet)


def count(sources: dict[str, str]) -> dict[str, int]:
    """Run the detector over a throwaway repo containing exactly these sources."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        for rel, text in sources.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        return ratchet.count_declarations(root)


class DuplicateHelperDetectionTests(unittest.TestCase):
    def test_counts_private_declaration(self):
        found = count({"src/A/Thing.cs":
                       "    private static string? NormalizeOptional(string? value)\n"
                       "        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();\n"})
        self.assertEqual(found, {"src/A/Thing.cs": 1})

    def test_counts_internal_and_public_declarations(self):
        found = count({
            "src/A/One.cs": "    internal static string RequireText(string? v) => v!;\n",
            "src/A/Two.cs": "    public static string? TrimOrNull(string? v) => v;\n",
        })
        self.assertEqual(found, {"src/A/One.cs": 1, "src/A/Two.cs": 1})

    def test_ignores_call_sites(self):
        """A call is not a declaration; only the latter is duplication."""
        found = count({"src/A/Caller.cs":
                       "        var a = NormalizeOptional(request.Id);\n"
                       "        var b = RequireText(request.Name);\n"
                       "        return FirstNonBlank(a, b);\n"})
        self.assertEqual(found, {})

    def test_ignores_the_canonical_home(self):
        found = count({"src/Meridian.Contracts/Text/TextPrimitives.cs":
                       "    public static string? NormalizeOptional(string? value) => value;\n"})
        self.assertEqual(found, {})

    def test_ignores_near_name_variants(self):
        """Renaming a divergent copy to say what it does is the fix, not a violation."""
        found = count({"src/A/Folding.cs":
                       "    private static string? NormalizeOptionalUpperInvariant(string? v) => v;\n"
                       "    private static string? NormalizeOptionalToken(string? v) => v;\n"})
        self.assertEqual(found, {})

    def test_counts_each_declaration_in_a_file(self):
        found = count({"src/A/Several.cs":
                       "    private static string? NormalizeOptional(string? v) => v;\n"
                       "    private static string RequireText(string? v) => v!;\n"
                       "    private static string? FirstNonEmpty(params string?[] v) => null;\n"})
        self.assertEqual(found, {"src/A/Several.cs": 3})

    def test_ignores_non_csharp_and_build_output(self):
        found = count({
            "src/A/notes.md": "private static string? NormalizeOptional(string? v) => v;\n",
            "src/A/obj/Generated.cs": "    private static string? NormalizeOptional(string? v) => v;\n",
            "src/A/bin/Copied.cs": "    private static string? NormalizeOptional(string? v) => v;\n",
        })
        self.assertEqual(found, {})


class TrackedHelperTests(unittest.TestCase):
    def test_tracks_the_shared_surface_and_its_aliases(self):
        self.assertEqual(
            set(ratchet.TRACKED_HELPERS),
            {"NormalizeOptional", "RequireText", "FirstNonBlank", "TrimOrNull", "FirstNonEmpty"},
        )

    def test_committed_baseline_matches_the_current_tree(self):
        """The checked-in baseline must describe reality, or the ratchet is decorative."""
        import json
        baseline = json.loads(ratchet.DEFAULT_BASELINE.read_text(encoding="utf-8"))
        current = ratchet.count_declarations(ratchet.REPO_ROOT)
        for rel, found in sorted(current.items()):
            self.assertIn(rel, baseline["files"], f"{rel} declares a tracked helper but is absent from the baseline")
            self.assertLessEqual(found, baseline["files"][rel], f"{rel} exceeds its baseline")


if __name__ == "__main__":
    unittest.main()
