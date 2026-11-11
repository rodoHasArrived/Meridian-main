"""Keep ``tests/scripts`` temporary directories tolerant of cleanup races.

``tempfile.TemporaryDirectory`` raises from its own cleanup when something is still writing
inside the directory - observed on the ``verify-workflows`` lane as
``OSError: [Errno 39] Directory not empty: '/tmp/.../.git'`` after a test whose assertions had
all passed. That turns a finished test red with no defect behind it, so every call site in this
directory passes ``ignore_cleanup_errors=True``. This suite fails when a new call site omits it.
"""

from __future__ import annotations

import ast
import unittest
from pathlib import Path


SCRIPT_TESTS = Path(__file__).resolve().parent
KEYWORD = "ignore_cleanup_errors"


def intolerant_calls(source: str) -> list[int]:
    """Return the line of each ``TemporaryDirectory(...)`` call that can raise during cleanup."""
    lines = []
    for node in ast.walk(ast.parse(source)):
        if not isinstance(node, ast.Call):
            continue
        func = node.func
        name = func.attr if isinstance(func, ast.Attribute) else getattr(func, "id", None)
        if name != "TemporaryDirectory":
            continue
        tolerant = any(
            keyword.arg == KEYWORD and isinstance(keyword.value, ast.Constant) and keyword.value.value is True
            for keyword in node.keywords
        )
        if not tolerant:
            lines.append(node.lineno)
    return sorted(lines)


class TemporaryDirectoryCleanupToleranceTests(unittest.TestCase):
    def test_every_script_test_temporary_directory_ignores_cleanup_errors(self):
        offenders = [
            f"{path.relative_to(SCRIPT_TESTS).as_posix()}:{line}"
            for path in sorted(SCRIPT_TESTS.rglob("*.py"))
            for line in intolerant_calls(path.read_text(encoding="utf-8"))
        ]
        self.assertEqual(
            offenders,
            [],
            "Pass ignore_cleanup_errors=True to tempfile.TemporaryDirectory in tests/scripts so a "
            "cleanup race cannot fail a test whose assertions passed.",
        )

    def test_detects_bare_attribute_and_imported_calls(self):
        source = (
            "import tempfile\n"
            "from tempfile import TemporaryDirectory\n"
            "a = tempfile.TemporaryDirectory()\n"
            "b = TemporaryDirectory(prefix='x')\n"
        )
        self.assertEqual(intolerant_calls(source), [3, 4])

    def test_accepts_only_a_literal_true(self):
        source = (
            "import tempfile\n"
            "a = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)\n"
            "b = tempfile.TemporaryDirectory(prefix='x', ignore_cleanup_errors=True)\n"
            "c = tempfile.TemporaryDirectory(ignore_cleanup_errors=False)\n"
            "d = tempfile.TemporaryDirectory(ignore_cleanup_errors=flag)\n"
        )
        self.assertEqual(intolerant_calls(source), [4, 5])


if __name__ == "__main__":
    unittest.main()
