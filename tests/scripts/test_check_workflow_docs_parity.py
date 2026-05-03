from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_workflow_docs_parity.py"
SPEC = importlib.util.spec_from_file_location("check_workflow_docs_parity", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["check_workflow_docs_parity"] = module
SPEC.loader.exec_module(module)


class CodeFenceRegexTests(unittest.TestCase):
    """Regression tests for CODE_FENCE_RE — was broken by escaped \\s in raw string."""

    def test_matches_plain_fence(self) -> None:
        self.assertIsNotNone(module.CODE_FENCE_RE.match("```"))

    def test_matches_bash_fence(self) -> None:
        m = module.CODE_FENCE_RE.match("```bash")
        self.assertIsNotNone(m)
        self.assertEqual(m.group("lang"), "bash")

    def test_matches_powershell_fence(self) -> None:
        m = module.CODE_FENCE_RE.match("```powershell")
        self.assertIsNotNone(m)
        self.assertEqual(m.group("lang"), "powershell")

    def test_matches_fence_with_trailing_whitespace(self) -> None:
        self.assertIsNotNone(module.CODE_FENCE_RE.match("```bash   "))

    def test_does_not_match_non_fence_line(self) -> None:
        self.assertIsNone(module.CODE_FENCE_RE.match("dotnet build"))

    def test_does_not_match_inline_code(self) -> None:
        self.assertIsNone(module.CODE_FENCE_RE.match("`single`"))


class SpecialContextRegexTests(unittest.TestCase):
    """Regression tests for SPECIAL_CONTEXT_RE — was broken by escaped \\b in raw string."""

    def test_matches_todo_keyword(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("This is a TODO item"))

    def test_matches_todo_at_line_start(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("TODO: fix this"))

    def test_matches_specialized(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("specialized setup required"))

    def test_matches_verify(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("verify current setup"))

    def test_matches_intended(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("intended workflow"))

    def test_matches_prerequisite(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("prerequisite is missing"))

    def test_matches_planned(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("planned feature"))

    def test_case_insensitive_todo(self) -> None:
        self.assertIsNotNone(module.SPECIAL_CONTEXT_RE.search("todo: lowercase"))

    def test_does_not_match_unrelated_text(self) -> None:
        self.assertIsNone(module.SPECIAL_CONTEXT_RE.search("make build"))


class ExtractExamplesTests(unittest.TestCase):
    """End-to-end: ParityChecker.extract_examples must find commands inside code fences."""

    def _make_checker(self) -> object:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "Makefile").write_text("build:\n\tdotnet build\n", encoding="utf-8")
            return module.ParityChecker(root)

    def test_extracts_command_from_bash_fence(self) -> None:
        checker = self._make_checker()
        with tempfile.TemporaryDirectory() as tmp:
            doc_path = Path(tmp) / "test.md"
            doc_path.write_text("Some docs\n\n```bash\nmake build\n```\n", encoding="utf-8")
            examples = checker.extract_examples(doc_path)
        self.assertTrue(any("make build" in ex.text for ex in examples), msg=f"Got: {[e.text for e in examples]}")

    def test_extracts_command_from_plain_fence(self) -> None:
        checker = self._make_checker()
        with tempfile.TemporaryDirectory() as tmp:
            doc_path = Path(tmp) / "test.md"
            doc_path.write_text("Some docs\n\n```\ndotnet restore\n```\n", encoding="utf-8")
            examples = checker.extract_examples(doc_path)
        self.assertTrue(any("dotnet restore" in ex.text for ex in examples), msg=f"Got: {[e.text for e in examples]}")

    def test_ignores_commands_outside_fence(self) -> None:
        checker = self._make_checker()
        with tempfile.TemporaryDirectory() as tmp:
            doc_path = Path(tmp) / "test.md"
            doc_path.write_text("Run `make build` to compile.\n", encoding="utf-8")
            examples = checker.extract_examples(doc_path)
        self.assertEqual(examples, [])


class ClassifyFindingTests(unittest.TestCase):
    """ParityChecker.classify must downgrade to warning when context contains special keywords."""

    def _make_checker(self) -> object:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "Makefile").write_text("build:\n\tdotnet build\n", encoding="utf-8")
            return module.ParityChecker(root)

    def _make_example(self, context: str) -> object:
        return module.CommandExample(
            file_path=Path("docs/HELP.md"),
            line_number=1,
            text="make nonexistent-target",
            context=context,
        )

    def test_classify_returns_fail_for_normal_context(self) -> None:
        checker = self._make_checker()
        example = self._make_example("Run this command to build the project")
        finding = checker.classify(example, "Unknown target", None)
        self.assertEqual(finding.severity, "fail")

    def test_classify_returns_warning_for_todo_context(self) -> None:
        checker = self._make_checker()
        example = self._make_example("TODO: verify current setup before using this command")
        finding = checker.classify(example, "Unknown target", None)
        self.assertEqual(finding.severity, "warning")

    def test_classify_returns_warning_for_specialized_context(self) -> None:
        checker = self._make_checker()
        example = self._make_example("This is a specialized workflow that requires manual setup")
        finding = checker.classify(example, "Unknown target", None)
        self.assertEqual(finding.severity, "warning")


if __name__ == "__main__":
    unittest.main()
