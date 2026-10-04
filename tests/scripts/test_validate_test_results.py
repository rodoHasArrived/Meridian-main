import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "validate-test-results.py"
SPEC = importlib.util.spec_from_file_location("validate_test_results", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class ValidateTestResultsTests(unittest.TestCase):
    def write_trx(self, root: Path, name: str, outcomes: list[tuple[str, str]]) -> None:
        results = "".join(
            f'<UnitTestResult testName="{test_name}" outcome="{outcome}" />'
            for test_name, outcome in outcomes
        )
        (root / name).write_text(
            f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{results}</Results></TestRun>',
            encoding="utf-8",
        )

    def test_accepts_passing_required_suites(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.write_trx(root, "meridian-integrations.trx", [("api", "Passed")])
            self.write_trx(root, "direct-lending-integrations.trx", [("ledger", "Passed")])

            evidence = MODULE.collect_evidence(
                root,
                ["meridian-integrations", "direct-lending-integrations"],
            )

            self.assertTrue(evidence["certifiable"])
            self.assertEqual(MODULE.validation_errors(evidence), [])

    def test_rejects_failed_skipped_and_unknown_outcomes(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.write_trx(
                root,
                "certification.trx",
                [("pass", "Passed"), ("fail", "Failed"), ("skip", "NotExecuted"), ("unknown", "Timeout")],
            )

            evidence = MODULE.collect_evidence(root)
            errors = MODULE.validation_errors(evidence)

            self.assertFalse(evidence["certifiable"])
            self.assertEqual(evidence["totals"], {"passed": 1, "failed": 1, "skipped": 1, "other": 1})
            self.assertEqual(len(errors), 3)

    def test_rejects_required_suite_without_passing_result(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.write_trx(root, "meridian-integrations.trx", [("api", "Passed")])

            evidence = MODULE.collect_evidence(root, ["missing-suite"])

            self.assertFalse(evidence["certifiable"])
            self.assertIn("required TRX suite 'missing-suite' produced no passing tests", MODULE.validation_errors(evidence))

    def test_rejects_empty_results_directory(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            with self.assertRaisesRegex(ValueError, "produced no TRX files"):
                MODULE.collect_evidence(Path(temporary_directory))

    def test_rejects_zero_discovery_even_when_the_other_required_suite_passes(self):
        for empty_prefix in ("meridian-integrations", "direct-lending-integrations"):
            with self.subTest(empty_prefix=empty_prefix), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                prefixes = ["meridian-integrations", "direct-lending-integrations"]
                for prefix in prefixes:
                    self.write_trx(root, f"{prefix}.trx", [] if prefix == empty_prefix else [("api", "Passed")])
                evidence = MODULE.collect_evidence(root, prefixes)
                self.assertFalse(evidence["certifiable"])
                self.assertIn(f"required TRX suite '{empty_prefix}' produced no passing tests", MODULE.validation_errors(evidence))

    def test_rejects_zero_discovery_without_required_prefixes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_trx(root, "empty.trx", [])
            evidence = MODULE.collect_evidence(root)
            self.assertFalse(evidence["certifiable"])
            self.assertIn("production certification executed no passing tests", MODULE.validation_errors(evidence))

    def test_rejects_empty_trx_beside_passing_trx_with_the_same_prefix(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_trx(root, "meridian-integrations_net10.trx", [("api", "Passed")])
            self.write_trx(root, "meridian-integrations_net11.trx", [])
            self.write_trx(root, "direct-lending-integrations_net10.trx", [("ledger", "Passed")])
            evidence = MODULE.collect_evidence(root, ["meridian-integrations", "direct-lending-integrations"])
            self.assertFalse(evidence["certifiable"])
            self.assertTrue(any("meridian-integrations_net11.trx' contains zero discovered tests" in error
                                for error in MODULE.validation_errors(evidence)))


if __name__ == "__main__":
    unittest.main()
