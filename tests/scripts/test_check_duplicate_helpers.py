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


FAKE_TEXT_PRIMITIVES = """
public static class TextPrimitives
{
    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string RequireText(string? value, string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be non-empty text.", paramName);
        }

        return value.Trim();
    }

    public static string? FirstNonBlank(params string?[] values)
    {
        if (values is null)
        {
            return null;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
"""


def clones(sources: dict[str, str]) -> dict[str, list[tuple[str, str]]]:
    """Run the body scan over a throwaway repo that owns the canonical helpers."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        staged = {"src/Meridian.Contracts/Text/TextPrimitives.cs": FAKE_TEXT_PRIMITIVES}
        staged.update(sources)
        for rel, text in staged.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        return ratchet.scan_body_clones(root)


class BodyCloneDetectionTests(unittest.TestCase):
    """A renamed copy must be recognised by what it does, not what it is called (#2702)."""

    def test_a_renamed_clone_is_detected(self):
        found = clones({"src/A/Thing.cs":
                        "    private static string? Clean(string? text)\n"
                        "        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("Clean", "NormalizeOptional")]})

    def test_a_braced_single_return_matches_the_expression_form(self):
        found = clones({"src/A/Thing.cs":
                        "    private static string? NullIfBlank(string? s)\n"
                        "    {\n"
                        "        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("NullIfBlank", "NormalizeOptional")]})

    def test_a_different_predicate_is_not_a_clone(self):
        # IsNullOrEmpty is a different function; member names are deliberately not folded.
        found = clones({"src/A/Thing.cs":
                        "    private static string? Clean(string? s)\n"
                        "        => string.IsNullOrEmpty(s) ? null : s.Trim();\n"})
        self.assertEqual(found, {})

    def test_a_behaviour_changing_copy_is_not_a_clone(self):
        # The historical failure mode: same shape plus case folding is a different function.
        found = clones({"src/A/Thing.cs":
                        "    private static string? Clean(string? s)\n"
                        "        => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();\n"})
        self.assertEqual(found, {})

    def test_a_guard_clone_matches_despite_a_different_message(self):
        # Exception message literals are folded: the helper is RequireText whatever it says.
        found = clones({"src/A/Thing.cs":
                        "    private static string RequireTrimmed(string value, string parameterName)\n"
                        "    {\n"
                        "        if (string.IsNullOrWhiteSpace(value))\n"
                        "        {\n"
                        "            throw new ArgumentException(\"Different message entirely.\", parameterName);\n"
                        "        }\n"
                        "\n"
                        "        return value.Trim();\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("RequireTrimmed", "RequireText")]})

    def test_a_loop_clone_matches_with_renamed_locals(self):
        found = clones({"src/A/Thing.cs":
                        "    private static string? PickFirst(params string?[] candidates)\n"
                        "    {\n"
                        "        if (candidates is null)\n"
                        "        {\n"
                        "            return null;\n"
                        "        }\n"
                        "\n"
                        "        foreach (var candidate in candidates)\n"
                        "        {\n"
                        "            if (!string.IsNullOrWhiteSpace(candidate))\n"
                        "            {\n"
                        "                return candidate.Trim();\n"
                        "            }\n"
                        "        }\n"
                        "\n"
                        "        return null;\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("PickFirst", "FirstNonBlank")]})

    def test_an_interpolated_message_running_code_is_not_a_clone(self):
        # The interpolation expression is executed code, not message text: a guard whose
        # message calls something can mutate state or throw, so folding it away would
        # report a behaviour-changing copy as exact.
        found = clones({"src/A/Thing.cs":
                        "    private static string RequireTrimmed(string value, string name)\n"
                        "    {\n"
                        "        if (string.IsNullOrWhiteSpace(value))\n"
                        "        {\n"
                        "            throw new ArgumentException($\"{RecordAndFormat(value)}\", name);\n"
                        "        }\n"
                        "\n"
                        "        return value.Trim();\n"
                        "    }\n"})
        self.assertEqual(found, {})

    def test_an_interpolated_message_of_plain_text_still_matches(self):
        found = clones({"src/A/Thing.cs":
                        "    private static string RequireTrimmed(string value, string name)\n"
                        "    {\n"
                        "        if (string.IsNullOrWhiteSpace(value))\n"
                        "        {\n"
                        "            throw new ArgumentException($\"plain text only\", name);\n"
                        "        }\n"
                        "\n"
                        "        return value.Trim();\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("RequireTrimmed", "RequireText")]})

    def test_an_explicitly_typed_foreach_clone_is_detected(self):
        # `foreach (string? candidate in ...)` is the same loop as `foreach (var value in ...)`.
        found = clones({"src/A/Thing.cs":
                        "    private static string? PickFirst(params string?[] candidates)\n"
                        "    {\n"
                        "        if (candidates is null)\n"
                        "        {\n"
                        "            return null;\n"
                        "        }\n"
                        "\n"
                        "        foreach (string? candidate in candidates)\n"
                        "        {\n"
                        "            if (!string.IsNullOrWhiteSpace(candidate))\n"
                        "            {\n"
                        "                return candidate.Trim();\n"
                        "            }\n"
                        "        }\n"
                        "\n"
                        "        return null;\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("PickFirst", "FirstNonBlank")]})

    def test_a_method_shaped_snippet_inside_a_string_is_not_a_declaration(self):
        # A generator template carrying helper-shaped text is data, not a declaration.
        found = clones({"src/A/Template.cs":
                        "    private const string Template = @\"\n"
                        "    private static string? Clean(string? text)\n"
                        "        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();\n"
                        "    \";\n"})
        self.assertEqual(found, {})

    def test_comment_markers_inside_a_string_do_not_corrupt_the_body(self):
        # `//` in a URL is text. Stripping it as a comment would truncate the real body
        # and let this clone (whose only difference is the folded literal) evade.
        found = clones({"src/A/Thing.cs":
                        "    private static string RequireTrimmed(string value, string name)\n"
                        "    {\n"
                        "        if (string.IsNullOrWhiteSpace(value))\n"
                        "        {\n"
                        "            throw new ArgumentException(\"see https://docs.example/rule\", name);\n"
                        "        }\n"
                        "\n"
                        "        return value.Trim();\n"
                        "    }\n"})
        self.assertEqual(found, {"src/A/Thing.cs": [("RequireTrimmed", "RequireText")]})

    def test_a_tracked_name_is_the_name_ratchets_jurisdiction(self):
        # One copy must never be counted by both detectors.
        found = clones({"src/A/Thing.cs":
                        "    private static string? NormalizeOptional(string? value)\n"
                        "        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();\n"})
        self.assertEqual(found, {})

    def test_the_canonical_home_is_not_a_clone_of_itself(self):
        self.assertEqual(clones({}), {})

    def test_committed_clone_baseline_matches_the_current_tree(self):
        """The checked-in body-clone baseline must describe reality too."""
        import json
        baseline = json.loads(ratchet.DEFAULT_BASELINE.read_text(encoding="utf-8"))
        allowed = baseline.get("body_clones", {})
        current = ratchet.scan_body_clones(ratchet.REPO_ROOT)
        for rel, entries in sorted(current.items()):
            self.assertLessEqual(
                len(entries),
                allowed.get(rel, 0),
                f"{rel} has renamed clone(s) not covered by the baseline: {entries}",
            )


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
