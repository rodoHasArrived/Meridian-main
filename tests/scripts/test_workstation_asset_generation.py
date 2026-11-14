"""Exercise CI-built bundles and standalone preparation without external tools."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

import yaml

ROOT = Path(__file__).resolve().parents[2]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


validator = load("workstation_assets", "build/scripts/ci/check-workstation-assets.py")
preparer = load("ensure_workstation", "build/scripts/ensure-workstation-assets.py")


class WorkstationAssetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.assets = self.root / preparer.ASSETS
        (self.assets / "assets").mkdir(parents=True)

    def bundle(self):
        (self.assets / "index.html").write_text('<script type="module" src="/workstation/assets/main.js"></script>')
        (self.assets / "assets/main.js").write_text('import("./lazy.js"); const styles=["assets/lazy.css"];')
        (self.assets / "assets/lazy.js").write_text('export const ready=true;')
        (self.assets / "assets/lazy.css").write_text('body {color: black}')

    def test_missing_entry_and_lazy_chunk_fail(self):
        self.assertTrue(validator.validate(self.assets))
        self.bundle()
        self.assertEqual(validator.validate(self.assets), [])
        (self.assets / "assets/lazy.js").unlink()
        self.assertTrue(any("lazy.js" in error for error in validator.validate(self.assets)))

    def test_missing_css_and_empty_entry_fail(self):
        self.bundle()
        (self.assets / "assets/lazy.css").unlink()
        self.assertTrue(any("lazy.css" in error for error in validator.validate(self.assets)))
        (self.assets / "index.html").write_text("<html>empty</html>")
        self.assertTrue(any("no script" in error for error in validator.validate(self.assets)))

    def test_asset_paths_cannot_escape_bundle(self):
        self.bundle()
        (self.assets / "assets/main.js").write_text('import("./../../outside.js");')
        self.assertTrue(any("escapes" in error for error in validator.validate(self.assets)))

    def test_standalone_lane_builds_only_when_missing_and_stops_on_failure(self):
        commands = []

        def run(command, **kwargs):
            commands.append(command)
            return subprocess.CompletedProcess(command, 0)

        self.assertEqual(preparer.ensure(self.root, run), 0)
        self.assertEqual(len(commands), 5)
        self.assertIn("ci", commands[0])
        self.assertIn("build", commands[-2])
        self.bundle()
        commands.clear()
        self.assertEqual(preparer.ensure(self.root, run), 0)
        self.assertEqual(len(commands), 1)
        self.assertIn("check-workstation-assets.py", commands[0][-1])
        (self.assets / "index.html").unlink()
        commands.clear()

        def fail(command, **kwargs):
            commands.append(command)
            return subprocess.CompletedProcess(command, 19)

        self.assertEqual(preparer.ensure(self.root, fail), 19)
        self.assertEqual(len(commands), 1)

    def test_demo_prepares_assets_before_dotnet_restore(self):
        workflow = yaml.load((ROOT / ".github/workflows/demo-smoke.yml").read_text(), Loader=yaml.BaseLoader)
        steps = workflow["jobs"]["demo-smoke"]["steps"]
        prepare = next(step for step in steps if "ensure-workstation-assets.py" in step.get("run", ""))
        restore = next(step for step in steps if "dotnet restore" in step.get("run", ""))
        self.assertLess(steps.index(prepare), steps.index(restore))


if __name__ == "__main__":
    unittest.main()
