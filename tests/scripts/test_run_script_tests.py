import importlib.util
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

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
