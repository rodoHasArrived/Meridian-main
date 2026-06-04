import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPO_ROOT / "build" / "scripts" / "generate-workspace-catalog-ts.py"


def _load_generator():
    spec = importlib.util.spec_from_file_location("generate_workspace_catalog_ts", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class WorkspaceCatalogTypeScriptGenerationTests(unittest.TestCase):
    def test_generated_dashboard_workspace_catalog_matches_csharp_contracts(self) -> None:
        generator = _load_generator()
        source = generator.SOURCE_FILE.read_text(encoding="utf-8")
        expected = generator.render(generator.extract_constants(source))
        actual = generator.OUTPUT_FILE.read_text(encoding="utf-8")

        self.assertEqual(
            expected,
            actual,
            "Run python build/scripts/generate-workspace-catalog-ts.py after changing WorkstationWorkspaceCatalog.cs.",
        )


if __name__ == "__main__":
    unittest.main()
