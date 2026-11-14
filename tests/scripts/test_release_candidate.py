from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from tests.scripts.test_release_audit_evidence import promotion


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


candidate = load('release_candidate_test_target', ROOT / 'build/scripts/ci/check-release-candidate.py')
publisher = load('candidate_publisher_test_target', ROOT / 'build/scripts/ci/publish-certified-release.py')
docs_automation = load('candidate_docs_test_profile', ROOT / 'build/scripts/docs/run-docs-automation.py')
COMMIT = 'a' * 40
RUN = '42'
ATTEMPT = '2'
NUMBER = '7'
VERSION = '0.1.0-rc.2'


class CandidateEvidenceApi:
    """Original artifact archives with real reports and API-advertised SHA-256 digests."""

    def __init__(self):
        self.run = dict(id=42, run_attempt=2, run_number=7, head_sha=COMMIT,
                        path='.github/workflows/desktop-installer-packaging.yml')
        identity = dict(commitSha=COMMIT, runId=RUN, runAttempt=ATTEMPT)
        source = dict(sourceCommit=COMMIT, drillSourceCommit=COMMIT,
                      manifestSha256='b' * 64, backupId='backup-42-2',
                      simulatedLossAtUtc='2026-11-14T12:00:00Z',
                      lossDeclaredAtUtc='2026-11-14T12:00:01Z')
        scripts = docs_automation.resolve_selected_scripts(argparse.Namespace(
            profile='core', scripts=None, auto_create_todos=False, include_pilot_readiness=False))
        self.reports = {name: {'report.json': {'artifact': name}}
                        for name in promotion.required_evidence(RUN, NUMBER, ATTEMPT, VERSION)}
        self.reports.update({
            f'release-signing-readiness-{RUN}-{ATTEMPT}': {
                'signing.json': dict(schemaVersion=1, **identity, status='passed',
                                     hasPrivateKey=True, publisherMatches=True)},
            f'production-recovery-drill-{RUN}-{ATTEMPT}': {
                'recovery-drill-receipt.json': source},
            f'production-recovery-objectives-{RUN}-{ATTEMPT}': {
                'recovery-objective-evaluation.json': dict(
                    source, objectiveStatus='proven', rpoStatus='proven', rtoStatus='proven',
                    objectiveErrors=[], measuredRpoSeconds=1.0, effectiveMaximumRpoSeconds=3600,
                    measuredRtoSeconds=32.0, effectiveMaximumRtoSeconds=7200,
                    operatorAcceptedBy='release-owner',
                    operatorAcceptanceEvidence='https://example.test/recovery/42/acceptance',
                    reconciliationEvidence='https://example.test/recovery/42/reconciliation')},
            f'production-certification-{NUMBER}-{ATTEMPT}': {
                'integration-receipt.json': dict(
                    identity=dict(commit=COMMIT, runId=RUN, runAttempt=ATTEMPT), result='success',
                    totals=dict(passed=37, failed=0, skipped=0, other=0),
                    suites={name: dict(passed=count, failed=0, skipped=0, other=0)
                            for name, count in [('meridian-integrations', 20),
                                                ('direct-lending-integrations', 10),
                                                ('demo-integrations', 7)]})},
            f'production-dependency-evidence-{NUMBER}-{ATTEMPT}': {
                'npm-audit-gate.json': dict(passed=True)},
            f'production-documentation-evidence-{NUMBER}-{ATTEMPT}': {
                'production-certification.json': dict(
                    profile='core', dry_run=False, selected_scripts=list(scripts),
                    results=[dict(name=name, status='success', return_code=0) for name in scripts])},
        })
        self.refresh()

    def refresh(self):
        self.rows, self.archives = [], {}
        for artifact_id, (name, reports) in enumerate(sorted(self.reports.items()), 1):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, 'w') as archive:
                for filename, report in sorted(reports.items()):
                    archive.writestr(filename, json.dumps(report))
            data = stream.getvalue()
            self.archives[artifact_id] = data
            self.rows.append(dict(id=artifact_id, name=name, expired=False, size_in_bytes=len(data),
                                  digest='sha256:' + hashlib.sha256(data).hexdigest(),
                                  workflow_run=dict(id=42, head_sha=COMMIT)))

    def json(self, endpoint):
        if 'attempts' in endpoint:
            return copy.deepcopy(self.run)
        return dict(total_count=len(self.rows), artifacts=copy.deepcopy(self.rows))

    def download(self, artifact, destination):
        destination.write_bytes(self.archives[artifact['id']])


class CandidateEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.api = CandidateEvidenceApi()
        self.gate = dict(
            schemaVersion=2, repository='test/repo', commitSha=COMMIT, runId=RUN, runAttempt=ATTEMPT,
            version=VERSION, supportScope='windows-x64-v0.1.0', supportedRuntimes=['win-x64'],
            validations={name: dict(result='success') for name in promotion.GATES}, lifecycle=[], assets={},
            installedStartup=dict(commitSha=COMMIT, workflowRunId=RUN, workflowRunAttempt=ATTEMPT,
                                  project='web-workstation', runtime='win-x64',
                                  validationLanes=['web-workstation-installed-startup']))
        for project, runtime in sorted(promotion.required_families(VERSION)):
            filename = 'Meridian-Setup.exe' if project == 'consumer-setup' else 'Meridian-win-x64.msix'
            (self.root / filename).write_bytes(f'certified {project} fixture'.encode())
            self.gate['lifecycle'].append(dict(
                project=project, runtime=runtime, architecture='x64', status='passed',
                sourceCommit=COMMIT, workflowRunId=RUN, workflowRunAttempt=ATTEMPT,
                mode='first-release', currentPackage=dict(name=filename, sha256=publisher.digest(self.root / filename))))
        self.collect()

    def collect(self):
        self.api.refresh()
        self.gate['upstreamEvidence'] = promotion.collect_upstream_evidence(
            self.root / publisher.BUNDLE_FILE, repository='test/repo', commit=COMMIT,
            run_id=RUN, number=NUMBER, attempt=ATTEMPT, needs=self.gate['validations'],
            api=self.api, version=VERSION)
        self.gate['assets'] = {path.name: publisher.digest(path) for path in self.root.iterdir()
                               if path.name != publisher.GATE_FILE}
        self.write_gate()

    def write_gate(self):
        (self.root / publisher.GATE_FILE).write_text(json.dumps(self.gate), encoding='utf-8')

    def report(self, prefix, filename):
        return self.api.reports[f'{prefix}-{RUN}-{ATTEMPT}'][filename]

    def numbered_report(self, prefix, filename):
        return self.api.reports[f'{prefix}-{NUMBER}-{ATTEMPT}'][filename]

    def evaluate(self):
        publisher.validate_audit_bundle(self.root / publisher.BUNDLE_FILE, self.gate)
        return candidate.evaluate(self.root, self.gate)

    def run_main(self):
        with patch.object(sys, 'argv', ['check-release-candidate.py', '--root', str(self.root),
                                       '--output', str(self.root / candidate.REPORT)]), patch('sys.stdout', io.StringIO()):
            candidate.main()

    def test_digest_bound_candidate_maps_the_six_p0_rows_on_one_commit(self):
        report = self.evaluate()
        self.assertEqual(report['status'], 'candidate-evidence-verified')
        self.assertEqual(report['commitSha'], COMMIT)
        self.assertEqual((report['runId'], report['runAttempt']), (RUN, ATTEMPT))
        self.assertEqual(set(report['rows']), {'PRD-000', 'PRD-013', 'PRD-014', 'PRD-015', 'PRD-016', 'PRD-017'})
        self.assertEqual(report['rows']['PRD-014']['packages'],
                         ['consumer-setup/win-x64', 'desktop-installer/win-x64'])
        self.assertEqual(report['rows']['PRD-016']['passedTests'], 37)
        self.assertTrue(report['limitations'])

    def test_signing_and_integration_reports_reject_another_commit_run_or_attempt(self):
        cases = [
            ('release-signing-readiness', 'signing.json', None, 'commitSha', 'c' * 40),
            ('release-signing-readiness', 'signing.json', None, 'runId', '43'),
            ('release-signing-readiness', 'signing.json', None, 'runAttempt', '1'),
            ('production-certification', 'integration-receipt.json', 'identity', 'commit', 'c' * 40),
            ('production-certification', 'integration-receipt.json', 'identity', 'runId', '43'),
            ('production-certification', 'integration-receipt.json', 'identity', 'runAttempt', '1'),
        ]
        for prefix, filename, nested, field, value in cases:
            with self.subTest(prefix=prefix, field=field):
                self.api = CandidateEvidenceApi()
                report = self.numbered_report(prefix, filename) if nested else self.report(prefix, filename)
                target = report[nested] if nested else report
                target[field] = value
                self.collect()
                with self.assertRaisesRegex(ValueError, 'another commit/run/attempt'):
                    self.evaluate()

    def test_failed_skipped_or_missing_dependencies_cannot_certify(self):
        original = copy.deepcopy(self.gate['validations'])
        for gate in promotion.GATES:
            for result in ('failure', 'cancelled', 'skipped', None):
                with self.subTest(gate=gate, result=result):
                    self.gate['validations'] = copy.deepcopy(original)
                    if result is None:
                        del self.gate['validations'][gate]
                    else:
                        self.gate['validations'][gate]['result'] = result
                    self.collect()
                    with self.assertRaisesRegex(ValueError, 'dependencies'):
                        self.evaluate()

    def test_unsigned_wrong_publisher_or_missing_private_key_refuses(self):
        signing = self.report('release-signing-readiness', 'signing.json')
        original = copy.deepcopy(signing)
        for change in (dict(status='failed'), dict(hasPrivateKey=False), dict(publisherMatches=False)):
            with self.subTest(change=change):
                signing.clear()
                signing.update(original | change)
                self.collect()
                with self.assertRaisesRegex(ValueError, 'PRD-014'):
                    self.evaluate()

    def test_missing_nested_report_cannot_be_inferred_from_successful_artifact(self):
        for prefix, filename in [('release-signing-readiness', 'signing.json'),
                                 ('production-recovery-objectives', 'recovery-objective-evaluation.json')]:
            with self.subTest(prefix=prefix):
                self.api = CandidateEvidenceApi()
                self.api.reports[f'{prefix}-{RUN}-{ATTEMPT}'] = {'unrelated.json': {}}
                self.collect()
                with self.assertRaisesRegex(ValueError, 'Missing.*' + filename):
                    self.evaluate()

    def test_recovery_objective_must_match_the_original_backup_identity(self):
        recovery = self.report('production-recovery-objectives', 'recovery-objective-evaluation.json')
        original = copy.deepcopy(recovery)
        for field, wrong in [('backupId', 'different-backup'), ('manifestSha256', 'c' * 64),
                             ('simulatedLossAtUtc', '2026-11-15T12:00:00Z'),
                             ('lossDeclaredAtUtc', '2026-11-15T12:00:01Z'),
                             ('sourceCommit', 'c' * 40), ('drillSourceCommit', 'c' * 40)]:
            with self.subTest(field=field):
                recovery.clear()
                recovery.update(original | {field: wrong})
                self.collect()
                with self.assertRaisesRegex(ValueError, 'PRD-015.*immutable'):
                    self.evaluate()

    def test_recovery_requires_proven_objectives_and_explicit_owner_acceptance(self):
        recovery = self.report('production-recovery-objectives', 'recovery-objective-evaluation.json')
        original = copy.deepcopy(recovery)
        for change in (dict(objectiveStatus='unproven'), dict(rpoStatus='unproven'),
                       dict(rtoStatus='unproven'), dict(objectiveErrors=['missing reconciliation']),
                       dict(operatorAcceptedBy=''), dict(operatorAcceptanceEvidence=' '),
                       dict(reconciliationEvidence=None)):
            with self.subTest(change=change):
                recovery.clear()
                recovery.update(original | change)
                self.collect()
                with self.assertRaisesRegex(ValueError, 'PRD-015'):
                    self.evaluate()

    def test_recovery_rejects_breached_nonfinite_boolean_or_nonpositive_budgets(self):
        recovery = self.report('production-recovery-objectives', 'recovery-objective-evaluation.json')
        original = copy.deepcopy(recovery)
        for measured, maximum, cap in [('measuredRpoSeconds', 'effectiveMaximumRpoSeconds', 3600),
                                       ('measuredRtoSeconds', 'effectiveMaximumRtoSeconds', 7200)]:
            for change in ({measured: cap + 1}, {measured: -1}, {measured: float('nan')},
                           {measured: float('inf')}, {measured: True}, {maximum: float('nan')},
                           {maximum: cap + 1}, {maximum: 0}, {maximum: '3600'}):
                with self.subTest(change=change):
                    recovery.clear()
                    recovery.update(original | change)
                    self.collect()
                    with self.assertRaisesRegex(ValueError, 'PRD-015 invalid or breached'):
                        self.evaluate()

    def test_zero_skip_trx_is_required_and_no_suite_can_be_omitted(self):
        integration = self.numbered_report('production-certification', 'integration-receipt.json')
        original = copy.deepcopy(integration)
        for changes in (dict(passed=0), dict(failed=1), dict(skipped=1), dict(other=1),
                        dict(passed=True), dict(skipped='0')):
            with self.subTest(changes=changes):
                integration.clear()
                integration.update(copy.deepcopy(original))
                integration['totals'].update(changes)
                self.collect()
                with self.assertRaisesRegex(ValueError, 'zero-skip TRX'):
                    self.evaluate()
        integration.clear()
        integration.update(copy.deepcopy(original))
        del integration['suites']['demo-integrations']
        self.collect()
        with self.assertRaisesRegex(ValueError, 'suites are incomplete'):
            self.evaluate()

    def test_docs_must_execute_every_core_check_without_failure_or_skip(self):
        docs = self.numbered_report('production-documentation-evidence', 'production-certification.json')
        original = copy.deepcopy(docs)
        mutations = [
            lambda value: value.update(dry_run=True),
            lambda value: value.update(profile='quick'),
            lambda value: value['results'][0].update(status='skipped'),
            lambda value: value['results'][0].update(return_code=1),
            lambda value: value['results'].pop(),
            lambda value: (value['results'].pop(), value['selected_scripts'].pop()),
        ]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                docs.clear()
                docs.update(copy.deepcopy(original))
                mutation(docs)
                self.collect()
                with self.assertRaisesRegex(ValueError, 'PRD-017'):
                    self.evaluate()

    def test_initial_envelope_requires_exactly_both_x64_lifecycle_families(self):
        original = copy.deepcopy(self.gate['lifecycle'])
        mutations = [
            lambda receipts: receipts.pop(),
            lambda receipts: receipts.append(copy.deepcopy(receipts[0])),
            lambda receipts: receipts[0].update(runtime='win-arm64'),
            lambda receipts: receipts[0].update(project='web-workstation'),
            lambda receipts: receipts[0].update(sourceCommit='c' * 40),
            lambda receipts: receipts[0].update(workflowRunAttempt='1'),
            lambda receipts: receipts[0].update(status='failed'),
        ]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                self.gate['lifecycle'] = copy.deepcopy(original)
                mutation(self.gate['lifecycle'])
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_raw_msix_lifecycle_schema_is_accepted_and_arm64_cannot_replace_x64(self):
        desktop = next(receipt for receipt in self.gate['lifecycle'] if receipt['project'] == 'desktop-installer')
        del desktop['project']
        del desktop['runtime']
        self.assertEqual(self.evaluate()['rows']['PRD-000']['runtimes'], ['win-x64'])
        desktop['architecture'] = 'ARM64'
        with self.assertRaisesRegex(ValueError, 'lifecycle families'):
            self.evaluate()

    def test_declared_envelope_cannot_change_version_policy(self):
        for change in (dict(supportScope='windows-x64-arm64'), dict(supportedRuntimes=['win-arm64']),
                       dict(supportedRuntimes=[]), dict(version='not-a-version')):
            with self.subTest(change=change), self.assertRaises(ValueError):
                candidate.evaluate(self.root, self.gate | change)

    def test_installed_startup_must_be_the_expected_family_commit_and_attempt(self):
        original = copy.deepcopy(self.gate['installedStartup'])
        for change in (dict(project='desktop-installer'), dict(runtime='win-arm64'),
                       dict(commitSha='c' * 40), dict(workflowRunId='43'),
                       dict(workflowRunAttempt='1'), dict(validationLanes=[])):
            with self.subTest(change=change):
                self.gate['installedStartup'] = original | change
                with self.assertRaises(ValueError):
                    self.evaluate()

    def test_candidate_writes_a_digest_bound_report_and_resume_preserves_its_bytes(self):
        self.run_main()
        before = {path.name: path.read_bytes() for path in self.root.iterdir()}
        self.run_main()
        self.assertEqual({path.name: path.read_bytes() for path in self.root.iterdir()}, before)
        gate = json.loads((self.root / publisher.GATE_FILE).read_text())
        self.assertEqual(gate['candidatePolicy'], candidate.POLICY)
        self.assertEqual(gate['assets'][candidate.REPORT], publisher.digest(self.root / candidate.REPORT))
        self.assertIn(candidate.REPORT, publisher.inventory(self.root, COMMIT, 'test/repo'))

    def test_publication_inventory_requires_the_versioned_candidate_policy_and_report(self):
        with self.assertRaisesRegex(ValueError, 'require the P0 candidate evidence gate'):
            publisher.inventory(self.root, COMMIT, 'test/repo')
        self.run_main()
        gate = json.loads((self.root / publisher.GATE_FILE).read_text())
        del gate['candidatePolicy']
        (self.root / publisher.GATE_FILE).write_text(json.dumps(gate))
        with self.assertRaisesRegex(ValueError, 'require the P0 candidate evidence gate'):
            publisher.inventory(self.root, COMMIT, 'test/repo')

    def test_publication_recomputes_candidate_report_even_if_its_new_digest_matches(self):
        self.run_main()
        report_path = self.root / candidate.REPORT
        report = json.loads(report_path.read_text())
        report['rows']['PRD-015']['measuredRpoSeconds'] = 0
        report_path.write_text(json.dumps(report))
        gate = json.loads((self.root / publisher.GATE_FILE).read_text())
        gate['assets'][candidate.REPORT] = publisher.digest(report_path)
        (self.root / publisher.GATE_FILE).write_text(json.dumps(gate))
        with self.assertRaisesRegex(ValueError, 'differs from retained certified evidence'):
            publisher.inventory(self.root, COMMIT, 'test/repo')

    def test_resume_refuses_to_replace_an_existing_different_candidate_report(self):
        (self.root / candidate.REPORT).write_text('{"status":"not-certified"}\n')
        original = (self.root / candidate.REPORT).read_bytes()
        with self.assertRaisesRegex(ValueError, 'Resume must retain'):
            self.run_main()
        self.assertEqual((self.root / candidate.REPORT).read_bytes(), original)

    def test_bundle_corruption_cannot_generate_candidate_evidence(self):
        bundle = self.root / publisher.BUNDLE_FILE
        bundle.write_bytes(bundle.read_bytes() + b'changed')
        with self.assertRaisesRegex(ValueError, 'digest changed'):
            self.run_main()
        self.assertFalse((self.root / candidate.REPORT).exists())


if __name__ == '__main__':
    unittest.main()
