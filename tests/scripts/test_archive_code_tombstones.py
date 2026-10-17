from __future__ import annotations

import tempfile
import unittest
from pathlib import Path


class ArchiveCodeTombstonesTests(unittest.TestCase):
    # Archive/code retains .NET and browser-workstation TypeScript source tombstones.
    CODE_EXTENSIONS = (".cs", ".fs", ".ts", ".tsx")
    TOMBSTONE_MARKER = "ARCHIVE TOMBSTONE"
    COMMENT_PREFIXES = ("//", "(*", "*)")

    def setUp(self) -> None:
        self.repo_root = Path(__file__).resolve().parents[2]
        self.archive_code_root = self.repo_root / "archive" / "code" / "src"
        self.normalized_archive_path = self.archive_code_root.relative_to(self.repo_root).as_posix().lower()

    def test_archive_code_files_are_comment_only_tombstones(self) -> None:
        # The archive was removed in 982eea2d7. Absence satisfies the rule; the
        # fixture tests below still exercise it if no archived files are present.
        self.assert_comment_only_tombstones(self.archive_code_root)

    def assert_comment_only_tombstones(self, archive_root: Path) -> None:
        files = sorted(
            file_path
            for file_path in archive_root.rglob("*")
            if file_path.is_file() and file_path.suffix.lower() in self.CODE_EXTENSIONS
        )

        for file_path in files:
            relative = file_path.relative_to(archive_root).as_posix()
            text = file_path.read_text(encoding="utf-8")

            self.assertIn(self.TOMBSTONE_MARKER, text, f"{relative} must contain the tombstone marker.")

            non_empty_lines = [line for line in text.splitlines() if line.strip()]
            for line in non_empty_lines:
                stripped = line.lstrip()
                self.assertTrue(
                    stripped.startswith(self.COMMENT_PREFIXES),
                    f"{relative} must remain comment-only (found non-comment line: {line!r}).",
                )

    def test_comment_only_tombstones_are_accepted_for_each_source_language(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            for extension in self.CODE_EXTENSIONS:
                (root / f"Tombstone{extension}").write_text(
                    "// ARCHIVE TOMBSTONE\n// Recover the former implementation from Git history.\n",
                    encoding="utf-8",
                )
            self.assert_comment_only_tombstones(root)

    def test_executable_source_is_rejected_for_each_source_language(self) -> None:
        for extension in self.CODE_EXTENSIONS:
            with self.subTest(extension=extension), tempfile.TemporaryDirectory() as temp_dir:
                root = Path(temp_dir)
                (root / f"Tombstone{extension}").write_text(
                    "// ARCHIVE TOMBSTONE\nlet restoredCode = 1;\n", encoding="utf-8"
                )
                with self.assertRaisesRegex(AssertionError, "must remain comment-only"):
                    self.assert_comment_only_tombstones(root)

    def test_comment_only_source_without_tombstone_marker_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "Tombstone.cs").write_text("// Historical source\n", encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "must contain the tombstone marker"):
                self.assert_comment_only_tombstones(root)

    def test_archive_code_paths_are_not_included_in_build_graph(self) -> None:
        candidate_files = [
            *self.repo_root.rglob("*.sln"),
            *self.repo_root.rglob("*.slnx"),
            *self.repo_root.rglob("*.csproj"),
            *self.repo_root.rglob("*.fsproj"),
            *self.repo_root.rglob("Directory.Build.props"),
            *self.repo_root.rglob("Directory.Build.targets"),
        ]

        for file_path in sorted(candidate_files):
            text = file_path.read_text(encoding="utf-8")
            normalized_text = text.replace("\\", "/").lower()
            relative = file_path.relative_to(self.repo_root).as_posix()
            self.assertNotIn(
                self.normalized_archive_path,
                normalized_text,
                f"{relative} must not include archived code path {self.normalized_archive_path!r}.",
            )


if __name__ == "__main__":
    unittest.main()
