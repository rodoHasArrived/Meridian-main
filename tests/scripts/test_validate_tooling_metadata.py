from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "validate-tooling-metadata.py"

spec = importlib.util.spec_from_file_location("validate_tooling_metadata", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules["validate_tooling_metadata"] = module
spec.loader.exec_module(module)


class ValidateToolingMetadataTests(unittest.TestCase):
    def test_validator_passes_current_repo_metadata(self) -> None:
        self.assertEqual(module.validate_tooling_metadata(ROOT), [])

    def test_main_returns_nonzero_for_synthetic_missing_path(self) -> None:
        missing = [module.ToolingReference("fixture metadata", "scripts/missing-helper.py")]
        with mock.patch.object(module, "load_package_references", return_value=missing), \
             mock.patch.object(module, "load_makefile_references", return_value=[]), \
             mock.patch.object(module, "load_dependabot_references", return_value=[]):
            self.assertEqual(module.main(["--root", str(ROOT)]), 1)

    def test_load_package_references_finds_node_and_prefix_targets(self) -> None:
        refs = module.load_package_references(ROOT)
        pairs = {(reference.path, reference.kind) for reference in refs}

        self.assertIn(("build/node/generate-icons.mjs", "path"), pairs)
        self.assertIn(("src/Meridian.Ui/dashboard", "directory"), pairs)

    def test_load_makefile_references_scans_includes_and_helpers(self) -> None:
        refs = module.load_makefile_references(ROOT)
        pairs = {(reference.source, reference.path) for reference in refs}

        self.assertIn(("Makefile include (Makefile)", "make/docs.mk"), pairs)
        self.assertIn(("Makefile helper (make/docs.mk)", "build/scripts/validate-tooling-metadata.py"), pairs)

    def test_node_tooling_entrypoints_are_canonical(self) -> None:
        self.assertEqual([], module.validate_canonical_node_entrypoints(ROOT))

    def test_dependabot_directories_are_loaded_with_stdlib_fallback(self) -> None:
        with mock.patch.object(module, "_try_import_yaml", return_value=None):
            directories = [reference.path for reference in module.load_dependabot_references(ROOT)]

        self.assertIn("/", directories)
        self.assertIn("/deploy/docker", directories)


if __name__ == "__main__":
    unittest.main()
