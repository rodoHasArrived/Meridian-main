import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPO_ROOT / "build" / "scripts" / "generate-ui-api-routes-ts.py"


def _load_generator():
    spec = importlib.util.spec_from_file_location("generate_ui_api_routes_ts", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class UiApiRoutesTypeScriptGenerationTests(unittest.TestCase):
    def test_generator_creates_and_checks_fresh_output_from_csharp_contracts(self) -> None:
        generator = _load_generator()
        source = generator.SOURCE_FILE.read_text(encoding="utf-8")
        expected = generator.render(generator.extract_routes(source))
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

    def test_generator_includes_recent_workstation_contracts(self) -> None:
        generator = _load_generator()
        routes = dict(generator.extract_routes(generator.SOURCE_FILE.read_text(encoding="utf-8")))

        self.assertEqual(
            "/api/workstation/assets/{securityId:guid}/operations",
            routes["WorkstationAssetOperations"],
        )
        self.assertEqual(
            "/api/ledger/journal-entry-workbench/submit-approval",
            routes["LedgerManualJournalEntrySubmitApproval"],
        )


if __name__ == "__main__":
    unittest.main()
