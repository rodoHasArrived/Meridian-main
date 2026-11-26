"""Shared guidance synchronization preserves host-specific text and fails closed."""

from __future__ import annotations

import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("generate_guidance", REPO / "build/scripts/ai/generate_guidance.py")
assert SPEC and SPEC.loader
guidance = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guidance)


class GuidanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.shared = b"<!-- shared-context:begin canonical -->\nShared instructions.\n<!-- shared-context:end -->"
        for index, name in enumerate((guidance.CANONICAL, *guidance.MIRRORS)):
            path = self.root / name
            path.parent.mkdir(parents=True)
            shared = self.shared if index == 0 else self.shared.replace(b"Shared instructions", b"Old instructions")
            path.write_bytes(f"Host {index}\r\n".encode() + shared + f"\r\nHost tail {index}\r\n".encode())

    def snapshot(self) -> dict[str, bytes]:
        return {name: (self.root / name).read_bytes() for name in (guidance.CANONICAL, *guidance.MIRRORS)}

    def test_sync_preserves_host_bytes_and_canonical(self) -> None:
        original = self.snapshot()
        self.assertEqual(guidance.generate(self.root, write=True), list(guidance.MIRRORS))
        self.assertEqual((self.root / guidance.CANONICAL).read_bytes(), original[guidance.CANONICAL])
        for name in guidance.MIRRORS:
            before, _, after = guidance.split_context(original[name], name)
            self.assertEqual((self.root / name).read_bytes(), before + self.shared + after)

    def test_check_is_read_only_and_idempotent_write_does_not_touch_mtime(self) -> None:
        original = self.snapshot()
        self.assertEqual(guidance.generate(self.root), list(guidance.MIRRORS))
        self.assertEqual(self.snapshot(), original)
        guidance.generate(self.root, write=True)
        mtimes = [(self.root / name).stat().st_mtime_ns for name in guidance.MIRRORS]
        self.assertEqual(guidance.generate(self.root, write=True), [])
        self.assertEqual([(self.root / name).stat().st_mtime_ns for name in guidance.MIRRORS], mtimes)

    def test_all_inputs_validated_before_any_write(self) -> None:
        for invalid in (
            b"no markers",
            self.shared + self.shared,
            b"<!-- shared-context:end -->\n<!-- shared-context:begin -->",
            b"<!-- shared-context:begin malformed\n<!-- shared-context:end -->",
            b"\xff" + self.shared,
        ):
            with self.subTest(invalid=invalid):
                (self.root / guidance.MIRRORS[-1]).write_bytes(invalid)
                before = self.snapshot()
                with self.assertRaises(ValueError):
                    guidance.generate(self.root, write=True)
                self.assertEqual(self.snapshot(), before)

    def test_missing_mirror_fails_without_creating_it(self) -> None:
        missing = self.root / guidance.MIRRORS[-1]
        missing.unlink()
        other = (self.root / guidance.MIRRORS[0]).read_bytes()
        with self.assertRaises(ValueError):
            guidance.generate(self.root, write=True)
        self.assertFalse(missing.exists())
        self.assertEqual((self.root / guidance.MIRRORS[0]).read_bytes(), other)

    def test_symlink_file_and_parent_rejected(self) -> None:
        path = self.root / guidance.MIRRORS[-1]
        original = path.read_bytes()
        target = self.root / "unrelated.md"
        target.write_bytes(original)
        path.unlink()
        try:
            path.symlink_to(target)
        except OSError as exc:
            self.skipTest(f"Symlink creation unavailable: {exc}")
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            guidance.generate(self.root, write=True)
        self.assertEqual(target.read_bytes(), original)
        path.unlink()
        path.parent.rmdir()
        path.parent.symlink_to(target.parent, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            guidance.generate(self.root, write=True)

    def test_cli_defaults_to_check_and_reports_malformed_as_error(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            self.assertEqual(guidance.main(["--root", str(self.root)]), 1)
        self.assertIn("--write", output.getvalue())
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(guidance.main(["--root", str(self.root), "--write"]), 0)
            self.assertEqual(guidance.main(["--root", str(self.root), "--check"]), 0)
        (self.root / guidance.CANONICAL).write_bytes(b"broken")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(guidance.main(["--root", str(self.root)]), 2)

    def test_script_entry_point_accepts_an_isolated_root(self) -> None:
        result = subprocess.run(
            [sys.executable, str(REPO / "build/scripts/ai/generate-guidance.py"), "--root", str(self.root), "--write"],
            text=True, capture_output=True, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(guidance.generate(self.root), [])


class SkillMirrorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / guidance.SKILL_SOURCE
        self.mirror = self.root / guidance.SKILL_MIRROR

    def put(self, base: Path, rel: str, data: bytes) -> None:
        path = base / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def test_missing_mirror_root_is_not_drift(self) -> None:
        self.put(self.source, "skill-a/SKILL.md", b"a")
        self.assertEqual(guidance.skill_mirror_drift(self.root), [])

    def test_reports_changed_missing_and_extra_files_then_repairs(self) -> None:
        self.put(self.source, "skill-a/SKILL.md", b"canonical")
        self.put(self.source, "skill-b/references/map.md", b"new")
        self.put(self.mirror, "skill-a/SKILL.md", b"stale")
        self.put(self.mirror, "skill-c/SKILL.md", b"orphan")
        expected = [f"{guidance.SKILL_MIRROR}/{rel}" for rel in
                    ("skill-a/SKILL.md", "skill-b/references/map.md", "skill-c/SKILL.md")]
        self.assertEqual(guidance.skill_mirror_drift(self.root), expected)
        self.assertEqual((self.mirror / "skill-a/SKILL.md").read_bytes(), b"stale")
        self.assertEqual(guidance.skill_mirror_drift(self.root, write=True), expected)
        self.assertEqual((self.mirror / "skill-a/SKILL.md").read_bytes(), b"canonical")
        self.assertEqual((self.mirror / "skill-b/references/map.md").read_bytes(), b"new")
        self.assertFalse((self.mirror / "skill-c/SKILL.md").exists())
        self.assertEqual(guidance.skill_mirror_drift(self.root), [])

    def test_excluded_and_cache_files_are_ignored(self) -> None:
        for rel in guidance.SKILL_MIRROR_EXCLUDES:
            self.put(self.source, rel, b"canonical-only")
        self.put(self.mirror, "_shared/project-context.md", b"host-specific")
        self.put(self.source, "skill-a/scripts/__pycache__/x.cpython-312.pyc", b"cache")
        self.assertEqual(guidance.skill_mirror_drift(self.root), [])


if __name__ == "__main__":
    unittest.main()
