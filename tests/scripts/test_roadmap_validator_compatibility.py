import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = ROOT / "tools" / "roadmap" / "validate_roadmap.py"


def load_script(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


validator = load_script("manual_roadmap_validator", SCRIPT_PATH)


VALID_LEGACY_CONTRACT = """
contract:
  statuses:
    - not_started
    - ready_for_acceptance
  transition_evidence_requirements:
    ready_for_acceptance:
      minimum_evidence_records: 1
      require_reviewer_metadata: true
      allowed_review_statuses:
        - approved
      required_evidence_types:
        - test
items:
  - id: LEGACY-001
    status: ready_for_acceptance
    evidence:
      - type: test
        path: tests/legacy-evidence.md
        produced_by: roadmap-compatibility-test
        produced_on: "2026-07-15"
        commit_sha: abcdef1
        reviewed_by: roadmap-reviewer
        review_status: approved
""".lstrip()


class RoadmapValidatorCompatibilityTests(unittest.TestCase):
    def test_canonical_repository_registry_uses_canonical_validation_rules(self) -> None:
        roadmap_path = ROOT / "docs" / "roadmap" / "data" / "roadmap-items.yml"

        result = validator.run_roadmap_validation(roadmap_path)

        self.assertEqual(0, result)

    def test_legacy_contract_remains_supported(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            roadmap_path = Path(temp_dir) / "legacy-roadmap.yml"
            roadmap_path.write_text(VALID_LEGACY_CONTRACT, encoding="utf-8")

            with mock.patch.object(validator, "_try_import_yaml", return_value=None):
                result = validator.run_roadmap_validation(roadmap_path)

        self.assertEqual(0, result)

    def test_legacy_contract_still_enforces_evidence_metadata(self) -> None:
        invalid_contract = VALID_LEGACY_CONTRACT.replace(
            "        reviewed_by: roadmap-reviewer\n",
            "",
        )
        with tempfile.TemporaryDirectory() as temp_dir:
            roadmap_path = Path(temp_dir) / "legacy-roadmap.yml"
            roadmap_path.write_text(invalid_contract, encoding="utf-8")
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                result = validator.run_roadmap_validation(roadmap_path)

        self.assertEqual(1, result)
        self.assertIn("missing required field 'reviewed_by'", stderr.getvalue())

    def test_malformed_legacy_yaml_is_not_silently_accepted_by_minimal_parser(self) -> None:
        class FailingYamlModule:
            class YAMLError(Exception):
                pass

            @staticmethod
            def safe_load(_text: str):
                raise FailingYamlModule.YAMLError("malformed legacy YAML")

        with tempfile.TemporaryDirectory() as temp_dir:
            roadmap_path = Path(temp_dir) / "malformed-legacy-roadmap.yml"
            roadmap_path.write_text(VALID_LEGACY_CONTRACT, encoding="utf-8")
            stderr = io.StringIO()

            with (
                mock.patch.object(validator, "_try_import_yaml", return_value=FailingYamlModule),
                contextlib.redirect_stderr(stderr),
            ):
                result = validator.run_roadmap_validation(roadmap_path)

        self.assertEqual(2, result)
        self.assertIn("malformed legacy YAML", stderr.getvalue())

    def test_canonical_shape_outside_repository_is_not_silently_validated_against_another_file(self) -> None:
        canonical_shape = """
schema:
  id: meridian.roadmap-items
  version: "1.0.0"
items: []
""".lstrip()
        with tempfile.TemporaryDirectory() as temp_dir:
            roadmap_path = Path(temp_dir) / "roadmap-items.yml"
            roadmap_path.write_text(canonical_shape, encoding="utf-8")
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                result = validator.run_roadmap_validation(roadmap_path)

        self.assertEqual(2, result)
        self.assertIn("inside a Meridian repository", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
