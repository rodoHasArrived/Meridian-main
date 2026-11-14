import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPO_ROOT / "build" / "scripts" / "generate-workspace-catalog-ts.py"


def _load_generator():
    spec = importlib.util.spec_from_file_location("generate_workspace_catalog_ts", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class WorkspaceCatalogTypeScriptGenerationTests(unittest.TestCase):
    def test_generator_creates_and_checks_fresh_output_from_csharp_contracts(self) -> None:
        generator = _load_generator()
        source = generator.SOURCE_FILE.read_text(encoding="utf-8")
        expected = generator.render(generator.extract_constants(source))
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
            root = Path(temporary)
            source_file = root / generator.SOURCE_FILE.relative_to(REPO_ROOT)
            output_file = root / generator.OUTPUT_FILE.relative_to(REPO_ROOT)
            source_file.parent.mkdir(parents=True)
            source_file.write_text(source, encoding="utf-8")
            output_file.parent.mkdir(parents=True)
            with patch.object(generator, "REPO_ROOT", root), patch.object(generator, "SOURCE_FILE", source_file), patch.object(generator, "OUTPUT_FILE", output_file):
                # A fresh checkout has no ignored mirror. Check must reject the
                # absence, generation must create it, and check must then pass.
                with patch("sys.argv", [str(GENERATOR_PATH), "--check"]):
                    self.assertEqual(1, generator.main())
                with patch("sys.argv", [str(GENERATOR_PATH)]):
                    self.assertEqual(0, generator.main())
                self.assertEqual(expected, output_file.read_text(encoding="utf-8"))
                with patch("sys.argv", [str(GENERATOR_PATH), "--check"]):
                    self.assertEqual(0, generator.main())
                    output_file.write_text("stale mirror\n", encoding="utf-8")
                    self.assertEqual(1, generator.main())

    def test_rejects_workspace_values_outside_product_maturity_taxonomy(self) -> None:
        generator = _load_generator()
        source = generator.SOURCE_FILE.read_text(encoding="utf-8")
        constants = generator.extract_constants(source)
        constants["DataBrowserMaturity"] = "Live"

        with self.assertRaisesRegex(
            ValueError,
            "DataBrowserMaturity must be one of Available, Preview, Setup",
        ):
            generator.render(constants)


if __name__ == "__main__":
    unittest.main()
