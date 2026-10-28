"""Validate observable task requirements and explicit ownership boundaries."""
from pathlib import Path
import sys
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parents[2] / "build/scripts/ai"
sys.path.insert(0, str(SCRIPTS))
from task_brief import validate_brief


class TaskBriefTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.brief = {
            "desiredBehavior": "Show why a command is blocked.",
            "acceptanceExample": "When readiness is absent, show a disabled command with the reason.",
            "ownedPaths": ["src/new_feature.py"],
            "exclusions": ["Do not change persistence or WPF behavior."],
            "validationCommands": ["python -m unittest tests.test_feature"],
        }

    def test_future_owned_file_allowed_and_prose_exclusions_preserved(self):
        result = validate_brief(self.root, self.brief)
        self.assertEqual(result["schemaVersion"], 1)
        self.assertEqual(result["ownedPaths"], ["src/new_feature.py"])
        self.assertEqual(result["exclusions"], self.brief["exclusions"])
        self.assertFalse((self.root / "src/new_feature.py").exists())

    def test_required_nonempty_fields_and_types(self):
        for field in self.brief:
            with self.subTest(field=field):
                missing = dict(self.brief)
                missing.pop(field)
                with self.assertRaisesRegex(ValueError, "Missing"):
                    validate_brief(self.root, missing)
        for field, value in (("desiredBehavior", " "), ("acceptanceExample", 42),
                             ("ownedPaths", []), ("ownedPaths", "src/file.py"),
                             ("exclusions", [""]), ("exclusions", "none"),
                             ("validationCommands", []), ("validationCommands", [42])):
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                validate_brief(self.root, {**self.brief, field: value})

    def test_empty_exclusions_allowed_and_unknown_fields_rejected(self):
        self.assertEqual(validate_brief(self.root, {**self.brief, "exclusions": []})["exclusions"], [])
        with self.assertRaisesRegex(ValueError, "Unknown"):
            validate_brief(self.root, {**self.brief, "acceptanceExamples": "typo"})
        for version in (True, 2, "1"):
            with self.subTest(version=version), self.assertRaisesRegex(ValueError, "schemaVersion"):
                validate_brief(self.root, {**self.brief, "schemaVersion": version})

    def test_owned_paths_reject_traversal_absolute_and_root(self):
        for path in ("../outside", "/etc/passwd", "C:\\Windows\\file", ".", "foo/../../bar"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                validate_brief(self.root, {**self.brief, "ownedPaths": [path]})

    def test_symlink_owned_path_escape_rejected(self):
        with tempfile.TemporaryDirectory() as outside:
            (self.root / "escape").symlink_to(outside, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "escapes"):
                validate_brief(self.root, {**self.brief, "ownedPaths": ["escape/future.py"]})

    def test_browser_and_wpf_require_explicit_shared_seam(self):
        for path in ("src/Meridian.Ui/dashboard/src/Screen.tsx", "src/Meridian.Wpf/ViewModels/New.cs"):
            with self.subTest(path=path):
                brief = {**self.brief, "ownedPaths": [path]}
                with self.assertRaisesRegex(ValueError, "sharedContract"):
                    validate_brief(self.root, brief)
                seam = "src/Meridian.Contracts/Readiness/CommandState.cs"
                self.assertEqual(validate_brief(self.root, {**brief, "sharedContract": seam})["sharedContract"], seam)
                explanation = "none: presentation-only spacing; commands and state stay unchanged"
                self.assertEqual(validate_brief(self.root, {**brief, "sharedContract": explanation})["sharedContract"], explanation)
                with self.assertRaisesRegex(ValueError, "explanation"):
                    validate_brief(self.root, {**brief, "sharedContract": "none:"})


if __name__ == "__main__":
    unittest.main()
