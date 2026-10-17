import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location(
    'benchmark_ci', Path(__file__).resolve().parents[2] / 'build/scripts/ci/benchmark-ci.py')
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)


class BenchmarkCiTests(unittest.TestCase):
    def test_local_provenance_does_not_impersonate_actions(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(
                benchmark.subprocess, 'check_output', side_effect=['a' * 40 + '\n', ' M changed.py\n']):
            identity = benchmark.provenance(True)
        self.assertEqual('a' * 40, identity['commitSha'])
        self.assertTrue(identity['worktreeDirty'])
        self.assertEqual('local', identity['executionEnvironment'])
        self.assertTrue(identity['runId'].startswith('local-'))
        self.assertTrue(identity['runnerLabel'].startswith('local-'))

    def test_hosted_metadata_is_required_without_local_flag(self):
        with patch.dict(os.environ, {}, clear=True):
            with self.assertRaisesRegex(ValueError, 'use --local'):
                benchmark.provenance(False)

    def run_pair(self, directory, pair, launch):
        output = Path(directory) / 'pair.json'
        identity = dict(commitSha='a' * 40, runId='local-test', runAttempt=1,
                        runnerLabel='local-Linux-x86_64', executionEnvironment='local')
        with patch.object(benchmark, 'provenance', return_value=identity), patch.object(
                benchmark.subprocess, 'run', side_effect=launch):
            code = benchmark.main(['--subject', 'browser', '--pair', str(pair),
                                   '--output', str(output), '--local'])
        return code, json.loads(output.read_text())

    def test_alternating_order_and_complete_fresh_evidence(self):
        calls = []

        def launch(command, **kwargs):
            env = kwargs['env']
            calls.append(int(env['DASHBOARD_VITEST_BATCH_SIZE']))
            directory = Path(env['MERIDIAN_BROWSER_RESULTS_DIR'])
            (directory / 'summary.json').write_text(json.dumps(dict(
                counts=dict(passed=10, failed=0, skipped=0, other=0),
                testIdentityDigest='digest', testSeconds=12.5)))
            return subprocess.CompletedProcess(command, 0)

        with tempfile.TemporaryDirectory() as directory:
            code, odd = self.run_pair(directory, 1, launch)
            self.assertEqual(0, code)
            code, even = self.run_pair(directory, 2, launch)
            self.assertEqual(0, code)
            self.assertEqual([8, 16, 16, 8], calls)
            self.assertEqual(['baseline', 'candidate'], odd['executionOrder'])
            self.assertEqual(['candidate', 'baseline'], even['executionOrder'])
            self.assertNotEqual(odd['baseline']['evidenceDirectory'], even['baseline']['evidenceDirectory'])
            self.assertEqual(12.5, even['candidate']['testSeconds'])

    def test_failed_launch_still_attempts_both_variants_and_does_not_use_stale_summary(self):
        calls = []

        def launch(command, **kwargs):
            calls.append(command)
            raise FileNotFoundError('npm is absent')

        with tempfile.TemporaryDirectory() as directory:
            stale = Path(directory) / 'baseline'
            stale.mkdir()
            (stale / 'summary.json').write_text('{"testSeconds": 1}')
            code, result = self.run_pair(directory, 1, launch)
            self.assertEqual(1, code)
            self.assertEqual(2, len(calls))
            for name in ('baseline', 'candidate'):
                self.assertEqual('failure', result[name]['conclusion'])
                self.assertEqual(127, result[name]['exitCode'])
                self.assertIsNone(result[name]['testSeconds'])
                self.assertIn('npm is absent', result[name]['launchError'])
                self.assertTrue(result[name]['evidenceError'])
                self.assertIn('npm is absent', (Path(result[name]['evidenceDirectory']) / 'command.log').read_text())

    def test_invalid_evidence_is_a_failure_even_when_command_succeeds(self):
        def launch(command, **kwargs):
            directory = Path(kwargs['env']['MERIDIAN_BROWSER_RESULTS_DIR'])
            (directory / 'summary.json').write_text('[]')
            return subprocess.CompletedProcess(command, 0)

        with tempfile.TemporaryDirectory() as directory:
            code, result = self.run_pair(directory, 1, launch)
            self.assertEqual(1, code)
            for name in ('baseline', 'candidate'):
                self.assertEqual('failure', result[name]['conclusion'])
                self.assertEqual(0, result[name]['exitCode'])
                self.assertIsNone(result[name]['testSeconds'])
                self.assertIn('counts', result[name]['evidenceError'])


if __name__ == '__main__':
    unittest.main()
