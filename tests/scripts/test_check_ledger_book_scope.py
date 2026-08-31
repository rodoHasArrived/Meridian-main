from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-ledger-book-scope.py"
SPEC = importlib.util.spec_from_file_location("check_ledger_book_scope", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_ledger_book_scope"] = guard
SPEC.loader.exec_module(guard)


def scan(sources: dict[str, str]) -> list[tuple[str, int, str]]:
    """Run the guard over a throwaway `src/` tree built from these files."""
    with tempfile.TemporaryDirectory() as temp_dir:
        source_root = Path(temp_dir) / "src"
        for rel, content in sources.items():
            path = source_root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        source_root.mkdir(parents=True, exist_ok=True)
        return guard.find_scope_coercions(source_root)


class CheckLedgerBookScopeTests(unittest.TestCase):
    def test_null_coalescing_to_empty_is_flagged(self) -> None:
        found = scan(
            {
                "Meridian.Ui.Shared/Services/BatchService.cs": """
internal static string BuildRoute(Draft draft)
    => BuildEvidenceRoute(draft.LedgerBookId ?? Guid.Empty, draft.PeriodId);
"""
            }
        )

        self.assertEqual(
            found,
            [("src/Meridian.Ui.Shared/Services/BatchService.cs", 3, "draft.LedgerBookId ?? Guid.Empty")],
        )

    def test_default_and_new_guid_spellings_are_flagged(self) -> None:
        found = scan(
            {
                "A.cs": "var a = draft.LedgerBookId ?? default;\n",
                "B.cs": "var b = draft.LedgerBookId ?? default(Guid);\n",
                "C.cs": "var c = draft.LedgerBookId ?? new Guid();\n",
                "D.cs": "var d = draft.LedgerBookId.GetValueOrDefault();\n",
            }
        )

        self.assertEqual([line for _, line, _ in found], [1, 1, 1, 1])
        self.assertEqual({rel for rel, _, _ in found}, {"src/A.cs", "src/B.cs", "src/C.cs", "src/D.cs"})

    def test_comparing_against_empty_is_the_correct_posture_and_is_not_flagged(self) -> None:
        """Rejecting an unscoped book is what the rest of the tree does. A guard that confused
        rejection with substitution would push authors away from the check it exists to encourage."""
        found = scan(
            {
                "Validator.cs": """
if (command.LedgerBookId == Guid.Empty)
{
    throw new InvalidOperationException("Accounting posting command ledger book id is required.");
}

if (draft.LedgerBookId is null || draft.LedgerBookId.Value == Guid.Empty)
{
    return Reject("ledger book is required");
}
"""
            }
        )

        self.assertEqual(found, [])

    def test_refusing_the_missing_book_is_not_flagged(self) -> None:
        """The remediation the guard's own failure text recommends must itself pass."""
        found = scan(
            {
                "BatchService.cs": """
var bookId = draft.LedgerBookId ?? throw new InvalidOperationException(
    "Daily valuation draft reached evidence-route construction with no ledger book.");
"""
            }
        )

        self.assertEqual(found, [])

    def test_an_unrelated_identifier_coerced_to_empty_is_out_of_scope(self) -> None:
        found = scan({"Other.cs": "var id = draft.CompanyId ?? Guid.Empty;\n"})

        self.assertEqual(found, [])

    def test_commented_out_coercion_is_not_flagged(self) -> None:
        found = scan(
            {
                "Doc.cs": """
// Not coerced: draft.LedgerBookId ?? Guid.Empty stamps a scope no reader accepts.
/// <remarks>draft.LedgerBookId ?? Guid.Empty was the old shape.</remarks>
var bookId = draft.LedgerBookId ?? throw new InvalidOperationException("required");
"""
            }
        )

        self.assertEqual(found, [])

    def test_build_output_directories_are_skipped(self) -> None:
        found = scan(
            {
                "Meridian.Ui.Shared/obj/Generated.cs": "var x = draft.LedgerBookId ?? Guid.Empty;\n",
                "Meridian.Ui.Shared/bin/Release/Copied.cs": "var y = draft.LedgerBookId ?? Guid.Empty;\n",
            }
        )

        self.assertEqual(found, [])

    def test_repository_source_keeps_every_accounting_path_ledger_book_native(self) -> None:
        """The live invariant (ACCT-CHECKLIST-01): no accounting path in this repo substitutes an
        empty ledger book for a missing one."""
        self.assertEqual(guard.find_scope_coercions(REPO_ROOT / "src"), [])

    def test_guard_exits_non_zero_and_names_the_offending_line(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_root = Path(temp_dir) / "src" / "Meridian.Ui.Shared" / "Services"
            source_root.mkdir(parents=True)
            (source_root / "BatchService.cs").write_text(
                "\nvar bookId = draft.LedgerBookId ?? Guid.Empty;\n", encoding="utf-8"
            )

            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), "--source-root", str(Path(temp_dir) / "src")],
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(completed.returncode, 1)
        self.assertIn("src/Meridian.Ui.Shared/Services/BatchService.cs:2", completed.stderr)

    def test_guard_exits_zero_on_the_repository(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(SCRIPT_PATH)], capture_output=True, text=True, check=False
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)


if __name__ == "__main__":
    unittest.main()
