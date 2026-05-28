from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "dev" / "validate_workstation_cockpit_acceptance_matrix.py"
SPEC = importlib.util.spec_from_file_location("validate_workstation_cockpit_acceptance_matrix", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["validate_workstation_cockpit_acceptance_matrix"] = module
SPEC.loader.exec_module(module)


def valid_matrix() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "criteria": [
            {
                "id": "criterion-one",
                "routeOwnership": [{"kind": "api", "route": "/api/workstation/example"}],
                "automatedTests": [{"project": "tests/Meridian.Tests", "filterableName": "ExampleTests_ShouldPass"}],
                "artifacts": [{"kind": "json", "path": "artifacts/workstation/example.json"}],
            }
        ],
    }


class WorkstationCockpitAcceptanceMatrixValidatorTests(unittest.TestCase):
    def test_valid_matrix_passes(self) -> None:
        self.assertEqual([], module.validate_matrix(valid_matrix()))

    def test_rejects_missing_route_ownership(self) -> None:
        matrix = valid_matrix()
        matrix["criteria"][0]["routeOwnership"] = []  # type: ignore[index]

        errors = module.validate_matrix(matrix)  # type: ignore[arg-type]

        self.assertTrue(any("missing routeOwnership" in error for error in errors))

    def test_rejects_missing_filterable_test_reference(self) -> None:
        matrix = valid_matrix()
        matrix["criteria"][0]["automatedTests"] = [{"project": "tests/Meridian.Tests", "filterableName": " "}]  # type: ignore[index]

        errors = module.validate_matrix(matrix)  # type: ignore[arg-type]

        self.assertTrue(any("automatedTests" in error and "filterableName" in error for error in errors))

    def test_rejects_missing_artifact_pointer(self) -> None:
        matrix = valid_matrix()
        matrix["criteria"][0]["artifacts"] = [{"kind": "screenshot", "path": ""}]  # type: ignore[index]

        errors = module.validate_matrix(matrix)  # type: ignore[arg-type]

        self.assertTrue(any("artifacts" in error and "path" in error for error in errors))

    def test_main_accepts_explicit_matrix_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            matrix_path = Path(tmp) / "matrix.json"
            matrix_path.write_text(
                '{"schemaVersion":1,"criteria":[{"id":"criterion-one","routeOwnership":[{"route":"/api/example"}],"automatedTests":[{"project":"tests/Meridian.Tests","filterableName":"Example"}],"artifacts":[{"path":"artifacts/example.json"}]}]}',
                encoding="utf-8",
            )

            self.assertEqual(0, module.main(["--matrix", str(matrix_path), "--summary"]))


if __name__ == "__main__":
    unittest.main()
