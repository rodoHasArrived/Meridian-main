import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
ROUTES_FILE = REPO_ROOT / "src/Meridian.Contracts/Api/UiApiRoutes.cs"
DOC_FILE = REPO_ROOT / "docs/operations/live-execution-controls.md"


def _extract_constant(name: str) -> str:
    text = ROUTES_FILE.read_text(encoding="utf-8")
    match = re.search(rf"public const string {name} = \"([^\"]+)\";", text)
    if match is None:
        raise AssertionError(f"contracts layer diverged: missing UiApiRoutes constant '{name}'")
    return match.group(1)


class LiveExecutionControlsRouteConsistencyTests(unittest.TestCase):
    def test_doc_routes_match_contracts_for_manual_overrides(self) -> None:
        doc_text = DOC_FILE.read_text(encoding="utf-8")

        expected_routes = {
            _extract_constant("ExecutionControlsManualOverrides"): "docs layer diverged from contracts for manual override create route",
            _extract_constant("ExecutionControlsManualOverrideClear"): "docs layer diverged from contracts for manual override clear route",
        }

        for route, error_message in expected_routes.items():
            if route not in doc_text:
                self.fail(f"{error_message}: expected '{route}' in {DOC_FILE}")


if __name__ == "__main__":
    unittest.main()
