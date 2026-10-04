import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'build/scripts/ci' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class EvidenceControlsTests(unittest.TestCase):
    def test_missing_empty_malformed_and_stale_trx_fail_even_when_process_succeeds(self):
        runner = load('run-dotnet-ci-tests')
        for evidence in (None, '<TestRun/>', '<broken', '<TestRun><UnitTestResult outcome="NotExecuted"/></TestRun>'):
            with self.subTest(evidence=evidence), tempfile.TemporaryDirectory() as tmp:
                output = Path(tmp) / 'required'
                output.mkdir()
                (output / 'required-old.trx').write_text('<TestRun><UnitTestResult outcome="Passed"/></TestRun>')
                def run(*args, **kwargs):
                    if evidence is not None:
                        (output / 'required.trx').write_text(evidence)
                    return type('Completed', (), {'returncode': 0})()
                with patch.object(runner.subprocess, 'run', side_effect=run):
                    result = runner.run_tests([runner.TestProject('required', 'test.csproj')], configuration='Release', test_filter='', results_dir=Path(tmp), dry_run=False)[0]
                self.assertNotEqual(result.exit_code, 0)
                self.assertIsNotNone(result.evidence_error)
                self.assertFalse((output / 'required-old.trx').exists())

    def test_trx_counts_and_identity_digest_ignore_run_guids_and_result_order(self):
        evidence = load('test_evidence')
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / 'slice.trx'
            rows = ['<UnitTestResult testName="first" outcome="Passed"/>', '<UnitTestResult testName="second" outcome="NotExecuted"/>']
            file.write_text('<TestRun>' + ''.join(rows) + '</TestRun>')
            first = evidence.collect_trx(Path(tmp), 'slice')
            file.write_text('<TestRun id="new">' + ''.join(reversed(rows)) + '</TestRun>')
            second = evidence.collect_trx(Path(tmp), 'slice')
            self.assertEqual(first['testIdentityDigest'], second['testIdentityDigest'])
            self.assertEqual(first['counts'], dict(passed=1, failed=0, skipped=1, other=0))

    def test_all_external_actions_including_subpaths_require_sha_and_version(self):
        hygiene = load('check-workflow-hygiene')
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            for ref, expected in [('org/repo/sub@v4 # v4', False), ('org/repo@' + 'a'*40, False), ('org/repo/sub@' + 'a'*40 + ' # v4', True), ('./local.yml', True)]:
                (directory / 'ci.yml').write_text('steps:\n  - uses: ' + ref + '\n')
                failures = []
                with patch.object(hygiene, 'WORKFLOW_DIR', directory), patch.object(hygiene, 'REPO_ROOT', directory):
                    hygiene.check_action_refs(failures)
                self.assertEqual(not failures, expected, ref)

    def test_untracked_or_expired_quarantine_is_rejected(self):
        runner = load('run-script-tests')
        valid = json.loads(runner.DEFAULT_QUARANTINE.read_text(encoding='utf-8'))
        name = next(iter(valid['quarantined_modules']))
        for bad in ('reason only', {'reason': 'no owner'}, dict(valid['quarantined_modules'][name], reviewBy='2000-01-01')):
            with tempfile.TemporaryDirectory() as tmp:
                file = Path(tmp) / 'quarantine.json'
                file.write_text(json.dumps({'quarantined_modules': {name: bad}}))
                with self.assertRaises(ValueError):
                    runner.load_quarantine(file)
