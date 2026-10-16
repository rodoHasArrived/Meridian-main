import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


SPEC = importlib.util.spec_from_file_location("generated_output", Path(__file__).resolve().parents[2] / "build/scripts/ci/check-generated-output.py")
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class GeneratedOutputTests(unittest.TestCase):
    def test_patch_includes_new_and_deleted_files_without_changing_real_index(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            def git(*args):
                return subprocess.check_output(["git", *args], cwd=root, stderr=subprocess.DEVNULL)

            git("init")
            git("config", "user.name", "Test")
            git("config", "user.email", "test@example.invalid")
            (root / ".gitignore").write_text("artifacts/\n")
            (root / "old.js").write_text("old\n")
            (root / "human.md").write_text("human\n")
            git("add", ".")
            git("commit", "-m", "fixture")
            (root / "old.js").unlink()
            (root / "new.js").write_text("new\n")
            (root / "human.md").write_text("staged user edit\n")
            git("add", "human.md")
            before = git("diff", "--cached", "--binary")
            self.assertFalse(module.check(root, root / "artifacts/docs"))
            self.assertEqual(before, git("diff", "--cached", "--binary"))
            patch = (root / "artifacts/docs/generated-repair.patch").read_text()
            self.assertIn("new file mode", patch)
            self.assertIn("deleted file mode", patch)
            self.assertEqual(["human.md", "new.js", "old.js"], json.loads((root / "artifacts/docs/drift.json").read_text())["changed_paths"])
            git("reset", "--hard", "HEAD")
            (root / "new.js").unlink()
            uml = root / "docs/diagrams/uml/advisory.svg"
            uml.parent.mkdir(parents=True)
            uml.write_text("advisory\n")
            self.assertTrue(module.check(root, root / "artifacts/docs"))

    def test_output_must_be_outside_generator_inputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError):
                module.check(root, root / "docs/status")
