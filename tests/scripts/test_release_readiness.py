from __future__ import annotations

from copy import deepcopy
from datetime import datetime, timedelta, timezone
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import yaml


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'build/scripts/ci/release-readiness.py'
spec = importlib.util.spec_from_file_location('release_readiness', SCRIPT)
readiness = importlib.util.module_from_spec(spec)
spec.loader.exec_module(readiness)


class ReleaseReadinessTests(unittest.TestCase):
    def setUp(self):
        self.now = datetime(2026, 10, 15, 12, tzinfo=timezone.utc)
        self.expected = dict(commitSha='a' * 40, runId='42', runAttempt='2')
        self.signing = dict(schemaVersion=1, **self.expected, status='passed', hasPrivateKey=True,
                            publisherMatches=True, observedAt=self.now.isoformat(),
                            notBefore=(self.now - timedelta(days=10)).isoformat(),
                            notAfter=(self.now + timedelta(days=90)).isoformat())
        self.needs = {name: {'result': 'success'} for name in readiness.GATES}
        self.gate = dict(schemaVersion=1, **self.expected, validations=deepcopy(self.needs), lifecycle=[], assets={},
                         installedStartup=dict(commitSha=self.expected['commitSha'], workflowRunId='42',
                                               workflowRunAttempt='2', project='web-workstation', runtime='win-x64',
                                               validationLanes=['web-workstation-installed-startup']))
        for project, runtime in sorted(readiness.promotion.FAMILIES):
            name = 'Meridian-Setup.exe' if project == 'consumer-setup' else f'Meridian-{runtime}.msix'
            self.gate['assets'][name] = 'b' * 64
            self.gate['lifecycle'].append(dict(project=project, runtime=runtime, architecture=runtime[4:],
                                               status='passed', sourceCommit=self.expected['commitSha'],
                                               workflowRunId='42', workflowRunAttempt='2',
                                               currentPackage=dict(name=name, sha256='b' * 64)))
        self.runners = dict(schemaVersion=1, **self.expected, observations={}, collectionError=False)
        for name, label in readiness.RUNNER_LABELS.items():
            self.runners['observations'][name] = dict(commitSha='c' * 40, runId='40', runAttempt='1',
                                                      conclusion='success', runnerName='GitHub Actions 7', labels=[label],
                                                      completedAt=(self.now - timedelta(days=1)).isoformat())

    def report(self, mode='coordinator'):
        return readiness.report(mode, self.needs, self.expected, self.signing, self.gate, self.runners, self.now)

    def test_complete_evidence_is_ready_but_never_authorizes_publication(self):
        result = self.report()
        self.assertEqual(result['status'], 'ready')
        self.assertFalse(result['publicationAuthorized'])
        self.assertEqual(len(result['checks']), len(readiness.GATES) + 2)

    def test_failed_cancelled_skipped_missing_and_unknown_gates_block(self):
        for gate in readiness.GATES:
            for outcome in ('failure', 'cancelled', 'skipped', 'pending', None):
                with self.subTest(gate=gate, outcome=outcome):
                    needs = deepcopy(self.needs)
                    if outcome is None:
                        del needs[gate]
                    else:
                        needs[gate]['result'] = outcome
                    result = readiness.report('coordinator', needs, self.expected, self.signing, self.gate, None, self.now)
                    self.assertEqual(result['status'], 'blocked')
                    item = next(item for item in result['checks'] if item['name'] == gate)
                    self.assertTrue(item['nextAction'])

    def test_successful_jobs_cannot_hide_missing_promotion_evidence(self):
        self.gate = None
        self.assertEqual(self.report()['status'], 'blocked')

    def test_promotion_receipt_requires_exact_commit_run_and_attempt(self):
        for key, invalid in (('commitSha', 'c' * 40), ('runId', '41'), ('runAttempt', '1')):
            with self.subTest(key=key):
                gate = deepcopy(self.gate)
                gate[key] = invalid
                self.assertEqual(readiness.promotion_status(gate, self.expected)['status'], 'blocked')

    def test_lifecycle_failure_missing_family_wrong_digest_and_startup_are_blockers(self):
        mutations = [
            lambda gate: gate['lifecycle'].pop(),
            lambda gate: gate['lifecycle'][0].update(status='failed'),
            lambda gate: gate['lifecycle'][0].update(workflowRunAttempt='1'),
            lambda gate: gate['lifecycle'][0]['currentPackage'].update(sha256='c' * 64),
            lambda gate: gate['installedStartup'].update(validationLanes=[]),
            lambda gate: gate['installedStartup'].update(validationLanes=None),
            lambda gate: gate['installedStartup'].update(commitSha='c' * 40),
            lambda gate: gate.update(validations={}),
            lambda gate: gate.update(assets={}),
        ]
        for mutation in mutations:
            gate = deepcopy(self.gate)
            mutation(gate)
            self.assertEqual(readiness.promotion_status(gate, self.expected)['status'], 'blocked')

    def test_missing_signing_evidence_is_unknown(self):
        self.signing = None
        self.assertEqual(self.report()['status'], 'unknown')

    def test_certificate_expiration_boundary_and_warning(self):
        for days, status in ((-1, 'blocked'), (0, 'blocked'), (1, 'warning'), (30, 'warning'), (31, 'ready')):
            with self.subTest(days=days):
                self.signing['notAfter'] = (self.now + timedelta(days=days)).isoformat()
                result = readiness.certificate_status(self.signing, self.expected, self.now)
                self.assertEqual(result['status'], status)
                if status != 'ready':
                    self.assertTrue(result['nextAction'])

    def test_not_yet_valid_wrong_publisher_missing_key_or_bad_dates_block(self):
        for changes in (dict(notBefore=(self.now + timedelta(days=1)).isoformat()), dict(publisherMatches=False),
                        dict(hasPrivateKey=False), dict(notAfter='invalid'), dict(notAfter='2027-01-01'),
                        dict(status='failed'), dict(runAttempt='1')):
            with self.subTest(changes=changes):
                receipt = dict(self.signing, **changes)
                self.assertEqual(readiness.certificate_status(receipt, self.expected, self.now)['status'], 'blocked')

    def test_old_certificate_snapshot_is_not_current_readiness(self):
        self.signing['observedAt'] = (self.now - timedelta(days=2)).isoformat()
        self.assertEqual(readiness.certificate_status(self.signing, self.expected, self.now)['status'], 'unknown')

    def test_runner_success_is_historical_observation_not_capacity_guarantee(self):
        result = self.report('preflight')
        self.assertEqual(result['status'], 'ready')
        for item in result['checks'][:-1]:
            self.assertEqual(item['status'], 'observed')
            self.assertIn('current capacity is not guaranteed', item['detail'])

    def test_declared_label_alone_missing_stale_or_failed_runner_proof_is_unknown(self):
        name = next(iter(readiness.RUNNER_JOBS))
        for change in (dict(runnerName=''), dict(completedAt=(self.now - timedelta(days=15)).isoformat()),
                       dict(conclusion='failure'), dict(labels=['windows-latest-unrecognized']),
                       dict(commitSha='abc'), dict(runAttempt='0'), dict(labels=None)):
            with self.subTest(change=change):
                snapshot = deepcopy(self.runners)
                snapshot['observations'][name].update(change)
                self.assertEqual(readiness.runner_status(snapshot, self.expected, self.now)[0]['status'], 'unknown')
        self.runners['observations'] = {}
        self.assertEqual(self.report('preflight')['status'], 'unknown')

    def test_runner_api_failure_remains_unknown(self):
        with patch.object(readiness, 'gh_json', side_effect=subprocess.CalledProcessError(1, 'gh')):
            snapshot = readiness.collect_runners('owner/repo', self.expected, self.now)
        self.assertTrue(snapshot['collectionError'])
        self.assertTrue(all(item['status'] == 'unknown' for item in readiness.runner_status(snapshot, self.expected, self.now)))

    def test_collector_uses_attempt_jobs_actual_assignment_and_ignores_unassigned_job(self):
        run = dict(id=41, run_attempt=3, head_sha='c' * 40, head_repository=dict(full_name='owner/repo'))
        jobs = []
        for index, (name, job_name) in enumerate(readiness.RUNNER_JOBS.items()):
            jobs.append(dict(id=index + 1, name=job_name, conclusion='success', runner_name='Actions 1',
                             labels=[readiness.RUNNER_LABELS[name]], started_at=(self.now - timedelta(hours=2)).isoformat(),
                             completed_at=(self.now - timedelta(hours=1)).isoformat()))
        jobs[0]['runner_name'] = ''
        with patch.object(readiness, 'gh_json', side_effect=[{'workflow_runs': [run]}, {'jobs': jobs}]) as api:
            snapshot = readiness.collect_runners('owner/repo', self.expected, self.now)
        self.assertFalse(snapshot['collectionError'])
        self.assertEqual(len(snapshot['observations']), 2)
        self.assertIn('/runs/41/attempts/3/jobs?', api.call_args_list[1].args[0])

    def test_collector_paginates_jobs_and_run_history(self):
        run = dict(id=41, run_attempt=3, head_sha='c' * 40, head_repository=dict(full_name='owner/repo'))
        # An entire page of unrelated repositories must not terminate collection early.
        wrong_repo = dict(run, head_repository=dict(full_name='another/repo'))
        name, job_name = next(iter(readiness.RUNNER_JOBS.items()))
        job = dict(id=1, name=job_name, conclusion='success', runner_name='Actions 1',
                   labels=[readiness.RUNNER_LABELS[name]], started_at=self.now.isoformat(), completed_at=self.now.isoformat())
        responses = [{'workflow_runs': [wrong_repo] * 100}, {'workflow_runs': [run]},
                     {'jobs': [{'name': 'unrelated'}] * 100}, {'jobs': [job]}]
        with patch.object(readiness, 'gh_json', side_effect=responses) as api:
            snapshot = readiness.collect_runners('owner/repo', self.expected, self.now)
        self.assertEqual(len(snapshot['observations']), 1)
        self.assertIn('page=2', api.call_args_list[1].args[0])
        self.assertIn('page=2', api.call_args_list[3].args[0])

    def test_identity_rejects_branch_names_zero_attempt_and_short_shas(self):
        for values in (('main', '42', '1'), ('a' * 40, '42', '0'), ('a' * 40, '-1', '1')):
            with self.assertRaises(ValueError):
                readiness.identity(*values)

    def test_duplicate_or_malformed_receipts_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            self.assertIsNone(readiness.read_receipt(path, 'signing.json'))
            (path / 'signing.json').write_text('{bad')
            self.assertEqual(readiness.read_receipt(path, 'signing.json'), {})
            (path / 'signing.json').write_text(json.dumps(self.signing))
            (path / 'duplicate').mkdir()
            (path / 'duplicate/signing.json').write_text(json.dumps(self.signing))
            self.assertEqual(readiness.read_receipt(path, 'signing.json'), {})

    def test_cli_persists_actionable_report_when_all_upstream_evidence_is_absent(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'report.json'
            environment = dict(os.environ, GITHUB_SHA='a' * 40, GITHUB_RUN_ID='42', GITHUB_RUN_ATTEMPT='2',
                               RELEASE_NEEDS='{}', GITHUB_STEP_SUMMARY=str(Path(directory) / 'summary.md'))
            result = subprocess.run(['python3', str(SCRIPT), 'summary', '--mode', 'coordinator',
                                     '--evidence-dir', directory, '--output', str(output)],
                                    env=environment, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1, result.stderr)
            self.assertEqual(json.loads(output.read_text())['status'], 'blocked')
            self.assertIn('Next action', output.with_suffix('.md').read_text())
            self.assertIn('missing', (Path(directory) / 'summary.md').read_text())


class ReleaseReadinessWorkflowTests(unittest.TestCase):
    def load(self, filename):
        return yaml.load((ROOT / '.github/workflows' / filename).read_text(), Loader=yaml.BaseLoader)

    def test_schedule_cannot_evict_pending_production_even_when_rehearsals_are_disabled(self):
        workflow = self.load('desktop-installer-packaging.yml')
        concurrency = workflow['concurrency']
        # Concurrency is selected before eligibility's opt-in condition. Only the
        # event decides the group, so enabled and disabled schedules are both isolated.
        self.assertEqual(concurrency['group'],
                         "${{ github.event_name == 'schedule' && 'desktop-installer-rehearsal' || 'desktop-installer-release' }}")
        self.assertNotIn('vars.', concurrency['group'])
        self.assertEqual(concurrency['cancel-in-progress'], 'false')

    def test_scheduled_rehearsals_are_opt_in_and_cannot_publish(self):
        workflow = self.load('desktop-installer-packaging.yml')
        self.assertIn('schedule', workflow['on'])
        self.assertIn("vars.MDC_ENABLE_RELEASE_REHEARSALS == 'true'", workflow['jobs']['eligibility']['if'])
        self.assertIn("github.ref == 'refs/heads/main'", workflow['jobs']['eligibility']['if'])
        self.assertIn("github.event_name != 'schedule'", workflow['jobs']['eligibility']['if'])
        publish = workflow['jobs']['release']['steps'][-1]
        self.assertEqual(publish['if'], "github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')")
        self.assertEqual(workflow['jobs']['eligibility']['environment'], 'desktop-release-signing')
        for job in workflow['jobs'].values():
            for step in job.get('steps', []):
                if step.get('uses', '').startswith('actions/checkout@'):
                    self.assertEqual(step['with']['ref'], '${{ github.sha }}')
                    self.assertEqual(step['with']['persist-credentials'], 'false')

    def test_summary_runs_after_failures_and_covers_complete_release_graph(self):
        workflow = self.load('desktop-installer-packaging.yml')
        job = workflow['jobs']['readiness-summary']
        self.assertIn('always()', job['if'])
        self.assertEqual(set(job['needs']), readiness.GATES)
        self.assertEqual(job['permissions'], {'contents': 'read', 'actions': 'read'})
        report = next(step for step in job['steps'] if 'run' in step)
        self.assertEqual(report['if'], 'always()')
        self.assertEqual(report['env']['RELEASE_NEEDS'], '${{ toJSON(needs) }}')
        self.assertEqual(job['steps'][-1]['if'], 'always()')
        self.assertIn('${{ github.run_attempt }}', job['steps'][-1]['with']['name'])

    def test_prerequisite_workflow_preserves_protected_signing_and_read_only_access(self):
        workflow = self.load('release-readiness.yml')
        self.assertEqual(set(workflow['on']), {'schedule', 'workflow_dispatch'})
        self.assertEqual(workflow['permissions'], {'contents': 'read'})
        self.assertEqual(workflow['jobs']['signing']['environment'], 'desktop-release-signing')
        self.assertEqual(workflow['jobs']['summary']['if'], 'always()')
        for job in workflow['jobs'].values():
            self.assertNotIn('write', job.get('permissions', {}).values())
            for step in job['steps']:
                self.assertNotIn('secrets: inherit', str(step))
                if step.get('uses', '').startswith('actions/checkout@'):
                    self.assertEqual(step['with']['ref'], '${{ github.sha }}')
                if 'run' in step:
                    self.assertNotIn('${{', step['run'])


if __name__ == '__main__':
    unittest.main()
