from __future__ import annotations

import unittest
from pathlib import Path


class ArchiveCodeTombstonesTests(unittest.TestCase):
    CODE_EXTENSIONS = (".cs", ".fs", ".vb", ".xaml", ".xml")

    def setUp(self) -> None:
        self.repo_root = Path(__file__).resolve().parents[2]
        self.archive_code_root = self.repo_root / "archive" / "code" / "src"

    def test_archive_code_files_are_comment_only_tombstones(self) -> None:
        files = sorted(
            file_path
            for file_path in self.archive_code_root.rglob("*")
            if file_path.is_file() and file_path.suffix.lower() in self.CODE_EXTENSIONS
        )
        self.assertNotEqual([], files, "Expected archived code tombstone files under archive/code/src.")

        for file_path in files:
            relative = file_path.relative_to(self.repo_root).as_posix()
            text = file_path.read_text(encoding="utf-8")

            self.assertIn("ARCHIVE TOMBSTONE", text, f"{relative} must contain the tombstone marker.")

            non_empty_lines = [line for line in text.splitlines() if line.strip()]
            for line in non_empty_lines:
                self.assertTrue(
                    line.lstrip().startswith("//"),
                    f"{relative} must remain comment-only (found non-comment line: {line!r}).",
                )

    def test_archive_code_paths_are_not_included_in_build_graph(self) -> None:
        candidate_files = [
            *self.repo_root.rglob("*.sln"),
            *self.repo_root.rglob("*.csproj"),
            *self.repo_root.rglob("*.fsproj"),
            *self.repo_root.rglob("Directory.Build.props"),
            *self.repo_root.rglob("Directory.Build.targets"),
        ]

        archive_token = self.archive_code_root.relative_to(self.repo_root).as_posix().lower()
        for file_path in sorted(set(candidate_files)):
            text = file_path.read_text(encoding="utf-8")
            normalized_text = text.replace("\\", "/").lower()
            relative = file_path.relative_to(self.repo_root).as_posix()
            self.assertNotIn(
                archive_token,
                normalized_text,
                f"{relative} must not include archived code path {archive_token!r}.",
            )


if __name__ == "__main__":
    unittest.main()
