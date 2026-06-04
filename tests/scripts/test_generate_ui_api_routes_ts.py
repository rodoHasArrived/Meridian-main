import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPO_ROOT / "build" / "scripts" / "generate-ui-api-routes-ts.py"


def _load_generator():
    spec = importlib.util.spec_from_file_location("generate_ui_api_routes_ts", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class UiApiRoutesTypeScriptGenerationTests(unittest.TestCase):
    def test_generated_dashboard_routes_match_csharp_contracts(self) -> None:
        generator = _load_generator()
        source = generator.SOURCE_FILE.read_text(encoding="utf-8")
        expected = generator.render(generator.extract_routes(source))
        actual = generator.OUTPUT_FILE.read_text(encoding="utf-8")

        self.assertEqual(
            expected,
            actual,
            "Run python build/scripts/generate-ui-api-routes-ts.py after changing UiApiRoutes.cs.",
        )

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
