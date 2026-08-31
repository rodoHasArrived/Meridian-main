import importlib.util
import sys
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "run-script-tests.py"
SPEC = importlib.util.spec_from_file_location("run_script_tests", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class RunScriptTestsTests(unittest.TestCase):
    def test_quarantine_manifest_parses_and_names_existing_modules(self):
        quarantined = MODULE.load_quarantine(MODULE.DEFAULT_QUARANTINE)

        self.assertGreater(len(quarantined), 0)
        scripts_dir = Path(__file__).resolve().parent
        for module_name, reason in quarantined.items():
            self.assertTrue(
                (scripts_dir / f"{module_name}.py").exists(),
                f"quarantine entry '{module_name}' names no file under tests/scripts; "
                "remove the entry if the suite was deleted",
            )
            self.assertTrue(reason.strip(), f"quarantine entry '{module_name}' needs a reason")

    def test_partition_excludes_quarantined_modules_and_keeps_the_rest(self):
        class FakeKept(unittest.TestCase):
            def runTest(self):  # pragma: no cover - never executed
                pass

        class FakeQuarantined(unittest.TestCase):
            def runTest(self):  # pragma: no cover - never executed
                pass

        FakeKept.__module__ = "test_kept_suite"
        FakeQuarantined.__module__ = "test_bad_suite"
        suite = unittest.TestSuite([FakeKept(), FakeQuarantined()])

        kept, excluded = MODULE.partition_suite(suite, {"test_bad_suite": "tracked"})

        self.assertEqual(excluded, {"test_bad_suite"})
        self.assertEqual(kept.countTestCases(), 1)

    def test_resolve_module_name_maps_loader_import_failures(self):
        # Mirrors unittest.loader._FailedTest's shape via public API: the loader emits a
        # TestCase in the unittest package whose test-method name is the unimportable
        # module's name.
        class FailedImportProxy(unittest.TestCase):
            def test_broken_module(self):  # pragma: no cover - never executed
                pass

        FailedImportProxy.__module__ = "unittest.loader"
        proxy = FailedImportProxy("test_broken_module")

        self.assertEqual(MODULE.resolve_module_name(proxy), "test_broken_module")


if __name__ == "__main__":
    unittest.main()
