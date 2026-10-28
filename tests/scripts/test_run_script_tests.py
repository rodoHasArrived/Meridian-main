import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "run-script-tests.py"
SPEC = importlib.util.spec_from_file_location("run_script_tests", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class RunScriptTestsTests(unittest.TestCase):
    def run_fixture(
        self, fixture_dir: Path, *extra_args: str, env_overrides: dict[str, str] | None = None
    ):
        quarantine = fixture_dir / "quarantine.json"
        quarantine.write_text('{"quarantined_modules": {}}', encoding="utf-8")
        env = os.environ.copy()
        env.pop("GITHUB_STEP_SUMMARY", None)
        if env_overrides:
            env.update(env_overrides)
        return subprocess.run(
            [
                sys.executable,
                str(SCRIPT_PATH),
                "--start-dir",
                str(fixture_dir),
                "--quarantine",
                str(quarantine),
                *extra_args,
            ],
            cwd=fixture_dir,
            env=env,
            capture_output=True,
            text=True,
            timeout=45,
        )

    def test_quarantine_manifest_parses_and_names_existing_modules(self):
        quarantined = MODULE.load_quarantine(MODULE.DEFAULT_QUARANTINE)

        manifest = json.loads(MODULE.DEFAULT_QUARANTINE.read_text(encoding="utf-8"))
        self.assertEqual(set(quarantined), set(manifest["quarantined_modules"]))
        scripts_dir = Path(__file__).resolve().parent
        for module_name, reason in quarantined.items():
            self.assertTrue(
                (scripts_dir / f"{module_name}.py").exists(),
                f"quarantine entry '{module_name}' names no file under tests/scripts; "
                "remove the entry if the suite was deleted",
            )
            self.assertTrue(reason.strip(), f"quarantine entry '{module_name}' needs a reason")

    def test_nonempty_quarantine_validates_metadata_and_module_existence(self):
        entry = {
            "reason": "Synthetic tracked defect",
            "owner": "@fixture-owner",
            "reviewBy": "2999-01-01",
            "tracking": "docs/engineering/ci-cd-optimization.md#script-test-quarantine",
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            quarantine = Path(temp_dir) / "quarantine.json"
            quarantine.write_text(json.dumps({
                "quarantined_modules": {"test_run_script_tests": entry}
            }), encoding="utf-8")
            loaded = MODULE.load_quarantine(quarantine)
            self.assertEqual(set(loaded), {"test_run_script_tests"})
            for value in entry.values():
                self.assertIn(value, loaded["test_run_script_tests"])

            quarantine.write_text(json.dumps({
                "quarantined_modules": {"test_nonexistent_quarantine_fixture": entry}
            }), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "missing test module"):
                MODULE.load_quarantine(quarantine)

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

    def test_default_workers_run_modules_concurrently_in_separate_processes(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            for name, peer in (("first", "second"), ("second", "first")):
                source = textwrap.dedent(
                    f"""\
                    import os
                    import time
                    import unittest
                    from pathlib import Path

                    class ConcurrentTests(unittest.TestCase):
                        def test_wait_for_peer(self):
                            directory = Path(__file__).parent
                            (directory / "{name}.ready").write_text(str(os.getpid()))
                            deadline = time.monotonic() + 10
                            while not (directory / "{peer}.ready").exists():
                                if time.monotonic() >= deadline:
                                    self.fail("peer module did not run concurrently")
                                time.sleep(0.02)
                            (directory / "{name}.completed").touch()
                    """
                )
                if name == "first":
                    source += textwrap.indent(
                        textwrap.dedent(
                            """
                            @unittest.skip("synthetic skip")
                            def test_skipped(self):
                                self.fail("skipped test must not execute")
                            """
                        ),
                        "    ",
                    )
                (fixture_dir / f"test_{name}.py").write_text(source, encoding="utf-8")

            result = self.run_fixture(fixture_dir)
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 0, output)
            for name in ("first", "second"):
                self.assertTrue((fixture_dir / f"{name}.completed").exists(), output)
            first_pid = int((fixture_dir / "first.ready").read_text())
            second_pid = int((fixture_dir / "second.ready").read_text())
            self.assertNotEqual(first_pid, second_pid, output)
            self.assertIn(
                "Script-test lane: ran 3 tests, 0 module(s) quarantined, "
                "failures=0, errors=0, skipped=1.",
                output,
            )

    def test_failures_and_import_errors_do_not_stop_other_modules(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_assertion.py").write_text(
                textwrap.dedent(
                    """\
                    import unittest

                    class FailingTests(unittest.TestCase):
                        def test_failure(self):
                            self.fail("synthetic assertion failure")
                    """
                ),
                encoding="utf-8",
            )
            (fixture_dir / "test_import.py").write_text(
                'raise RuntimeError("synthetic import failure")\n', encoding="utf-8"
            )
            (fixture_dir / "test_success.py").write_text(
                textwrap.dedent(
                    """\
                    import unittest
                    from pathlib import Path

                    class SuccessfulTests(unittest.TestCase):
                        def test_success(self):
                            Path(__file__).with_suffix(".completed").touch()
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(fixture_dir, "--workers", "2")
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 1, output)
            self.assertTrue((fixture_dir / "test_success.completed").exists(), output)
            self.assertIn("synthetic assertion failure", output)
            self.assertIn("synthetic import failure", output)
            self.assertIn(
                "Script-test lane: ran 3 tests, 0 module(s) quarantined, "
                "failures=1, errors=1, skipped=0.",
                output,
            )

    def test_one_worker_still_uses_a_fresh_process_for_each_module(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            for name in ("first", "second"):
                (fixture_dir / f"test_{name}.py").write_text(
                    textwrap.dedent(
                        """\
                        import os
                        import unittest
                        from pathlib import Path

                        class ProcessTests(unittest.TestCase):
                            def test_record_process(self):
                                Path(__file__).with_suffix(".pid").write_text(str(os.getpid()))
                        """
                    ),
                    encoding="utf-8",
                )

            result = self.run_fixture(fixture_dir, "--workers", "1")
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 0, output)
            first_pid = int((fixture_dir / "test_first.pid").read_text())
            second_pid = int((fixture_dir / "test_second.pid").read_text())
            self.assertNotEqual(first_pid, second_pid, output)
            self.assertIn(
                "Script-test lane: ran 2 tests, 0 module(s) quarantined, "
                "failures=0, errors=0, skipped=0.",
                output,
            )

    def test_workers_prioritize_repository_packages_after_parent_path_changes(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            repo_root = SCRIPT_PATH.parents[3]
            ai_scripts = repo_root / "build" / "scripts" / "ai"
            (fixture_dir / "test_repository_import.py").write_text(
                textwrap.dedent(
                    f"""\
                    import unittest
                    from pathlib import Path
                    from tests.scripts import workflow_assertions

                    class RepositoryImportTests(unittest.TestCase):
                        def test_repository_package(self):
                            self.assertEqual(
                                Path(workflow_assertions.__file__).resolve(),
                                Path({str(repo_root / 'tests/scripts/workflow_assertions.py')!r}),
                            )
                    """
                ),
                encoding="utf-8",
            )
            (fixture_dir / "test_z_parent_path.py").write_text(
                textwrap.dedent(
                    f"""\
                    import multiprocessing
                    import sys
                    import unittest
                    from pathlib import Path

                    # Full parent discovery imports unrelated suites that prepend
                    # this directory, which contains its own 'tests' package.
                    if multiprocessing.current_process().name == "MainProcess":
                        sys.path.insert(0, {str(ai_scripts)!r})
                        Path(__file__).with_suffix(".path").write_text(
                            "\\n".join(sys.path), encoding="utf-8"
                        )

                    class ParentPathTests(unittest.TestCase):
                        def test_kept_in_discovery(self):
                            pass
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(fixture_dir, "--workers", "1")
            output = result.stdout + result.stderr
            parent_path = (fixture_dir / "test_z_parent_path.path").read_text(
                encoding="utf-8"
            ).splitlines()

            self.assertEqual(parent_path[0], str(ai_scripts), output)
            self.assertGreater(parent_path.index(str(repo_root)), 0, output)
            self.assertEqual(result.returncode, 0, output)
            self.assertIn(
                "Script-test lane: ran 2 tests, 0 module(s) quarantined, "
                "failures=0, errors=0, skipped=0.",
                output,
            )

    def test_fixture_summary_writes_do_not_pollute_the_job_summary(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            summary_path = fixture_dir / "job-summary.md"
            summary_path.write_text("Existing job evidence.\n", encoding="utf-8")
            (fixture_dir / "test_summary.py").write_text(
                textwrap.dedent(
                    """\
                    import os
                    import unittest
                    from pathlib import Path

                    class SummaryTests(unittest.TestCase):
                        def test_append_fixture_evidence(self):
                            summary = Path(os.environ["GITHUB_STEP_SUMMARY"])
                            with summary.open("a", encoding="utf-8") as output:
                                output.write("synthetic fixture summary evidence\\n")
                            self.assertIn(
                                "synthetic fixture summary evidence",
                                summary.read_text(encoding="utf-8"),
                            )
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(
                fixture_dir, env_overrides={"GITHUB_STEP_SUMMARY": str(summary_path)}
            )
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 0, output)
            self.assertIn("Script-test lane: ran 1 tests", output)
            summary = summary_path.read_text(encoding="utf-8")
            self.assertTrue(summary.startswith("Existing job evidence.\n"), summary)
            self.assertIn("### Script quarantine", summary)
            self.assertNotIn("synthetic fixture summary evidence", summary)

    def test_worker_rejects_tests_disappearing_after_parent_discovery(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_disappearing.py").write_text(
                textwrap.dedent(
                    """\
                    import unittest
                    from pathlib import Path

                    class DisappearingTests(unittest.TestCase):
                        def test_should_not_run(self):
                            self.fail("discovery fixture should not run")

                    source = Path(__file__)
                    source.with_suffix(".discovered").touch()
                    source.write_text("# No tests remain after parent discovery.\\n", encoding="utf-8")
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(
                fixture_dir, env_overrides={"PYTHONDONTWRITEBYTECODE": "1"}
            )
            output = result.stdout + result.stderr

            self.assertTrue((fixture_dir / "test_disappearing.discovered").exists(), output)
            self.assertEqual(result.returncode, 1, output)
            self.assertIn(
                "Test discovery changed for test_disappearing: expected 1 tests, found 0", output
            )
            self.assertIn(
                "Script-test lane: ran 0 tests, 0 module(s) quarantined, "
                "failures=0, errors=1, skipped=0.",
                output,
            )

    def test_spawned_modules_keep_repository_imports_ahead_of_ai_test_package(self):
        root = SCRIPT_PATH.parents[3]
        ai_scripts = root / "build/scripts/ai"
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_a_pollute.py").write_text(textwrap.dedent(f"""\
                import sys
                import unittest
                from tests.scripts import workflow_assertions

                # Parent discovery imports the repository helper, then inherits
                # this path order into fresh spawn interpreters without its cache.
                sys.path.insert(0, {str(ai_scripts)!r})

                class PollutingTests(unittest.TestCase):
                    def test_helper(self):
                        self.assertEqual(workflow_assertions.__file__, {str(root / 'tests/scripts/workflow_assertions.py')!r})
                """), encoding="utf-8")
            (fixture_dir / "test_z_consume.py").write_text(textwrap.dedent("""\
                import unittest
                from tests.scripts.workflow_assertions import assert_pinned_action

                class ConsumerTests(unittest.TestCase):
                    def test_first(self):
                        self.assertTrue(callable(assert_pinned_action))

                    def test_second(self):
                        self.assertEqual(assert_pinned_action.__module__, "tests.scripts.workflow_assertions")
                """), encoding="utf-8")
            result = self.run_fixture(fixture_dir, "--verbosity", "2", env_overrides={
                "PYTHONPATH": os.pathsep.join((str(ai_scripts), str(root))),
                "PYTHONDONTWRITEBYTECODE": "1",
            })
            output = result.stdout + result.stderr
            self.assertEqual(result.returncode, 0, output)
            for test_id in ("test_a_pollute.PollutingTests.test_helper",
                            "test_z_consume.ConsumerTests.test_first", "test_z_consume.ConsumerTests.test_second"):
                self.assertIn(test_id, output)
            self.assertIn("Script-test lane: ran 3 tests, 0 module(s) quarantined, failures=0, errors=0, skipped=0.", output)

    def test_worker_entry_repairs_path_order_before_discovering_a_module(self):
        root = SCRIPT_PATH.parents[3]
        module_name = "test_worker_repository_import_fixture"
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / f"{module_name}.py").write_text(textwrap.dedent(f"""\
                import sys
                import unittest
                from importlib.machinery import PathFinder
                origin = PathFinder.find_spec("tests", sys.path).origin
                if origin != {str(root / 'tests/__init__.py')!r}:
                    raise ImportError("repository package is shadowed: " + str(origin))
                from tests.scripts.workflow_assertions import assert_pinned_action

                class WorkerTests(unittest.TestCase):
                    def test_helper(self):
                        self.assertTrue(callable(assert_pinned_action))
                """), encoding="utf-8")
            self.addCleanup(sys.modules.pop, module_name, None)
            with patch.object(sys, "path", [str(root / "build/scripts/ai"), *sys.path]):
                result = MODULE.run_module(module_name, str(fixture_dir), 1,
                                           [f"{module_name}.WorkerTests.test_helper"])
            self.assertTrue(result["successful"], result["output"])
            self.assertEqual(result["testsRun"], 1)

    def test_worker_import_failure_reports_original_error_without_relaxing_ids(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_worker_import.py").write_text(textwrap.dedent("""\
                import multiprocessing
                import unittest
                if multiprocessing.current_process().name != "MainProcess":
                    raise ImportError("synthetic worker-only missing dependency")

                class ImportTests(unittest.TestCase):
                    def test_first(self):
                        pass

                    def test_second(self):
                        pass
                """), encoding="utf-8")
            result = self.run_fixture(fixture_dir)
            output = result.stdout + result.stderr
            self.assertEqual(result.returncode, 1, output)
            self.assertIn("Test discovery changed for test_worker_import: expected 2 tests, found 1", output)
            self.assertIn("Discovery import errors:", output)
            self.assertIn("synthetic worker-only missing dependency", output)
            self.assertIn("failures=0, errors=1, skipped=0.", output)

    def test_abrupt_worker_exit_fails_with_a_useful_diagnostic(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_crashing.py").write_text(
                textwrap.dedent(
                    """\
                    import os
                    import unittest

                    class CrashingTests(unittest.TestCase):
                        def test_exit_without_a_result(self):
                            os._exit(17)
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(fixture_dir)
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 1, output)
            self.assertIn("Script module crashed: test_crashing", output)
            self.assertIn("BrokenProcessPool", output)
            self.assertIn("terminated", output.lower())
            self.assertIn(
                "Script-test lane: ran 0 tests, 0 module(s) quarantined, "
                "failures=0, errors=1, skipped=0.",
                output,
            )

    def test_system_exit_in_class_setup_cannot_report_success(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            fixture_dir = Path(temp_dir)
            (fixture_dir / "test_exiting.py").write_text(
                textwrap.dedent(
                    """\
                    import unittest

                    class ExitingTests(unittest.TestCase):
                        @classmethod
                        def setUpClass(cls):
                            raise SystemExit(0)

                        def test_should_not_run(self):
                            self.fail("class setup should have exited")
                    """
                ),
                encoding="utf-8",
            )

            result = self.run_fixture(fixture_dir)
            output = result.stdout + result.stderr

            self.assertEqual(result.returncode, 1, output)
            self.assertIn("Script module crashed: test_exiting", output)
            self.assertIn("SystemExit: 0", output)
            self.assertIn(
                "Script-test lane: ran 0 tests, 0 module(s) quarantined, "
                "failures=0, errors=1, skipped=0.",
                output,
            )

    def test_workers_must_be_positive(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            result = self.run_fixture(Path(temp_dir), "--workers", "0")

        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("--workers", result.stderr)
        self.assertIn("at least 1", result.stderr)


if __name__ == "__main__":
    unittest.main()
