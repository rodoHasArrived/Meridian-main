import importlib.util
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "validate-tooling-metadata.py"

spec = importlib.util.spec_from_file_location("validate_tooling_metadata", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ValidateToolingMetadataTests(unittest.TestCase):
    def test_load_makefile_paths_scans_root_and_modular_makefiles(self) -> None:
        paths = module.load_makefile_paths()

        self.assertTrue(any(source == "Makefile" for source, _ in paths))
        self.assertTrue(any(source.startswith("make/") for source, _ in paths))
        self.assertIn(
            ("make/docs.mk", "build/scripts/validate-tooling-metadata.py"),
            paths,
        )


if __name__ == "__main__":
    unittest.main()
