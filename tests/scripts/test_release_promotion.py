import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import yaml

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'build/scripts/ci' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReleasePromotionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.gate = load('verify-release-promotion')
        self.preflight = load('release-preflight')
        self.receipts = self.root / 'receipts'
        self.receipts.mkdir()
        self.needs = {name: {'result': 'success'} for name in self.gate.GATES}
        self.manifests = []
        for project, runtime in sorted(self.gate.FAMILIES):
            family = self.root / project / runtime
            family.mkdir(parents=True)
            package = family / (f'Meridian.Desktop-{runtime}.msix' if project == 'desktop-installer' else 'Meridian-Setup.exe')
            package.write_bytes(b'certified bytes')
            sbom = family / f'{project}-{runtime}-sbom.spdx.json'
            sbom.write_text('{}')
            manifest = family / f'{project}-{runtime}-release-evidence.json'
            manifest.write_text(json.dumps(dict(project=project, runtime=runtime, commitSha='abc', workflowRunId='42', workflowRunAttempt='2',
                                                artifactRoot=family.as_posix(), files=[dict(path=p.as_posix(), sha256=self.gate.digest(p)) for p in (package, sbom)])))
            self.manifests.append(manifest)
            checksums = family / f'{project}-{runtime}-SHA256SUMS'
            checksums.write_text('\n'.join(f'{self.gate.digest(p)}  {p.name}' for p in (package, sbom, manifest)))
            if project == 'desktop-installer':
                receipt = self.receipts / f'desktop-installer-{runtime}-lifecycle.json'
                receipt.write_text(json.dumps(dict(status='passed', sourceCommit='abc', workflowRunId='42', workflowRunAttempt='2', architecture=runtime[4:], mode='first-release', publisherTrust='pre-trusted-certificate-chain',
                                                   currentPackage=dict(name=package.name, sha256=self.gate.digest(package)),
                                                   steps=[dict(name=name, status='passed') for name in ('trust-publisher', 'install-current', 'launch-current', 'repair-current', 'launch-after-repair', 'uninstall')])) )
            else:
                self.consumer_receipt = self.receipts / 'consumer-setup-win-x64-lifecycle.json'
                self.consumer_receipt.write_text(json.dumps(dict(
                    schemaVersion=1, project='consumer-setup', runtime='win-x64', architecture='x64',
                    status='passed', sourceCommit='abc', workflowRunId='42', workflowRunAttempt='2',
                    mode='first-release', publisherTrust='pre-trusted-certificate-chain',
                    currentPackage=dict(name=package.name, sha256=self.gate.digest(package)), priorPackage=None, priorReleaseTag=None,
                    predecessor=dict(schemaVersion=1, project='consumer-setup', runtime='win-x64', sourceCommit='abc',
                                     workflowRunId='42', workflowRunAttempt='2', currentTag='v1.0.0', firstRelease=True,
                                     priorReleaseTag=None, priorPackage=None, eligibleReleaseCount=2, consumerReleaseCount=0,
                                     reason='No eligible production release has a consumer EXE.'),
                    steps=[dict(name=name, status='passed') for name in (
                        'verify-signature', 'verify-payload', 'clean-runner', 'install-current', 'launch-current',
                        'bundled-database-ready', 'repair-current', 'launch-after-repair', 'restart-current',
                        'launch-after-restart', 'uninstall', 'preserve-data')]
                        + [dict(name=name, status='not-applicable', detail='First consumer release: no published consumer EXE exists.')
                           for name in ('install-prior', 'launch-prior', 'update-current', 'rollback-prior', 'launch-after-rollback')],
                )))
        self.smoke = self.root / 'startup.json'
        self.smoke.write_text(json.dumps(dict(commitSha='abc', workflowRunId='42', workflowRunAttempt='2', project='web-workstation', runtime='win-x64', validationLanes=['web-workstation-installed-startup'])))

    def verify(self):
        return self.gate.verify(self.root, self.receipts, self.smoke, 'abc', '42', '2', self.needs)

    def n_minus_one_consumer_receipt(self):
        receipt = json.loads(self.consumer_receipt.read_text())
        receipt['mode'] = 'n-1-update'
        receipt['priorPackage'] = dict(name='Meridian-Setup.exe', sha256='a' * 64)
        receipt['priorReleaseTag'] = 'v0.9.0'
        receipt['predecessor'].update(firstRelease=False, consumerReleaseCount=1, priorReleaseTag='v0.9.0',
                                      priorPackage=dict(receipt['priorPackage'], path='prior/Meridian-Setup.exe'))
        receipt['steps'].extend(dict(name=name, status='passed') for name in ('restore-current', 'launch-after-restore'))
        return receipt

    def test_success_promotes_exact_digest_evidence_for_both_architectures_and_consumer(self):
        assets, receipts, startup = self.verify()
        self.assertEqual(len(assets), 12)
        self.assertEqual(len(receipts), 3)
        self.assertIn('Meridian-Setup.exe', assets)
        consumer = next(receipt for receipt in receipts if receipt.get('project') == 'consumer-setup')
        self.assertEqual(consumer['currentPackage']['sha256'], assets['Meridian-Setup.exe']['sha256'])
        self.assertEqual(startup['runtime'], 'win-x64')

    def test_msix_only_lifecycle_and_web_startup_evidence_cannot_promote_consumer_exe(self):
        self.consumer_receipt.unlink()
        with self.assertRaisesRegex(ValueError, 'consumer lifecycle certification receipt'):
            self.verify()

    def test_consumer_receipt_cannot_be_replaced_with_msix_evidence(self):
        desktop = next(self.receipts.glob('desktop-installer-*.json'))
        self.consumer_receipt.write_text(desktop.read_text())
        with self.assertRaisesRegex(ValueError, 'Consumer lifecycle evidence'):
            self.verify()

    def test_consumer_receipt_identity_and_attempt_are_bound_to_publication(self):
        original = json.loads(self.consumer_receipt.read_text())
        mismatches = {
            'schemaVersion': (None, 2, True, '1'), 'project': (None, 'desktop-installer'),
            'runtime': (None, 'win-arm64'), 'architecture': (None, 'arm64'),
            'status': (None, 'failed', 'skipped'), 'sourceCommit': (None, 'other-commit'),
            'workflowRunId': (None, 'other-run'), 'workflowRunAttempt': (None, '1'),
            'publisherTrust': (None, 'runner-trusted-self-signed (validation only)'),
            'mode': (None, 'manual-skip'),
        }
        for field, values in mismatches.items():
            for value in values:
                with self.subTest(field=field, value=value):
                    self.consumer_receipt.write_text(json.dumps(dict(original, **{field: value})))
                    with self.assertRaises(ValueError):
                        self.verify()

    def test_consumer_receipt_requires_exact_exe_digest_and_artifact_name(self):
        original = json.loads(self.consumer_receipt.read_text())
        desktop = next(self.root.rglob('*.msix'))
        sbom = next(self.root.rglob('consumer-setup-*-sbom.spdx.json'))
        for package in (None, {}, dict(name='Meridian-Setup.exe', sha256='uncertified'),
                        dict(name=desktop.name, sha256=self.gate.digest(desktop)),
                        dict(name=sbom.name, sha256=self.gate.digest(sbom))):
            with self.subTest(package=package):
                self.consumer_receipt.write_text(json.dumps(dict(original, currentPackage=package)))
                with self.assertRaisesRegex(ValueError, 'exact package'):
                    self.verify()

    def test_consumer_failed_missing_or_duplicate_lifecycle_steps_block_promotion(self):
        original = json.loads(self.consumer_receipt.read_text())
        for required in (step['name'] for step in original['steps'] if step['status'] == 'passed'):
            for change in ('missing', 'failed', 'skipped', 'not-applicable', 'duplicate'):
                with self.subTest(required=required, change=change):
                    value = json.loads(json.dumps(original))
                    row = next(step for step in value['steps'] if step['name'] == required)
                    if change == 'missing':
                        value['steps'].remove(row)
                    elif change == 'duplicate':
                        value['steps'].append(dict(row, status='failed'))
                    else:
                        row['status'] = change
                    self.consumer_receipt.write_text(json.dumps(value))
                    with self.assertRaises(ValueError):
                        self.verify()

    def test_consumer_first_release_requires_explicit_predecessor_exceptions(self):
        original = json.loads(self.consumer_receipt.read_text())
        for prior_step in (step['name'] for step in original['steps'] if step['status'] == 'not-applicable'):
            for change in ('missing', 'passed', 'failed', 'no-reason', 'blank-reason'):
                with self.subTest(prior_step=prior_step, change=change):
                    value = json.loads(json.dumps(original))
                    row = next(step for step in value['steps'] if step['name'] == prior_step)
                    if change == 'missing':
                        value['steps'].remove(row)
                    elif change == 'no-reason':
                        row.pop('detail')
                    elif change == 'blank-reason':
                        row['detail'] = ' '
                    else:
                        row['status'] = change
                    self.consumer_receipt.write_text(json.dumps(value))
                    with self.assertRaises(ValueError):
                        self.verify()

    def test_consumer_n_minus_one_mode_requires_upgrade_and_rollback_to_pass(self):
        receipt = self.n_minus_one_consumer_receipt()
        self.consumer_receipt.write_text(json.dumps(receipt))
        with self.assertRaisesRegex(ValueError, 'first-release exception'):
            self.verify()
        for step in receipt['steps']:
            step['status'] = 'passed'
        self.consumer_receipt.write_text(json.dumps(receipt))
        self.assertEqual(len(self.verify()[1]), 3)
        receipt['steps'] = [step for step in receipt['steps'] if step['name'] != 'rollback-prior']
        self.consumer_receipt.write_text(json.dumps(receipt))
        with self.assertRaisesRegex(ValueError, 'predecessor evidence'):
            self.verify()

    def test_consumer_predecessor_resolution_is_bound_to_family_commit_and_attempt(self):
        original = json.loads(self.consumer_receipt.read_text())
        mismatches = {'schemaVersion': (None, True, 2), 'project': (None, 'desktop-installer'),
                      'runtime': (None, 'win-arm64'), 'sourceCommit': (None, 'other-commit'),
                      'workflowRunId': (None, 'other-run'), 'workflowRunAttempt': (None, '1'),
                      'firstRelease': (None, False, 'true'), 'eligibleReleaseCount': (None, -1, True),
                      'consumerReleaseCount': (None, -1, True, 1), 'reason': (None, '', ' '),
                      'priorReleaseTag': ('v0.9.0',), 'priorPackage': ({'name': 'Meridian-Setup.exe'},)}
        for field, values in mismatches.items():
            for value in values:
                with self.subTest(field=field, value=value):
                    receipt = json.loads(json.dumps(original))
                    receipt['predecessor'][field] = value
                    self.consumer_receipt.write_text(json.dumps(receipt))
                    with self.assertRaises(ValueError):
                        self.verify()
        for field in ('priorPackage', 'priorReleaseTag'):
            receipt = dict(original, **{field: 'unexpected predecessor'})
            self.consumer_receipt.write_text(json.dumps(receipt))
            with self.assertRaisesRegex(ValueError, 'first-release exception'):
                self.verify()

    def test_consumer_n_minus_one_requires_the_resolved_prior_package_and_release_tag(self):
        original = self.n_minus_one_consumer_receipt()
        for step in original['steps']:
            step['status'] = 'passed'
        changes = (
            ('priorPackage', None), ('priorReleaseTag', None), ('priorReleaseTag', 'eval-v0.9.0'),
            ('priorReleaseTag', 'v0.8.0'), ('priorPackage', dict(name='desktop.msix', sha256='a' * 64)),
            ('priorPackage', dict(name='Meridian-Setup.exe', sha256='b' * 64)),
            ('priorPackage', dict(name='Meridian-Setup.exe', sha256='invalid')),
        )
        for field, value in changes:
            with self.subTest(field=field, value=value):
                self.consumer_receipt.write_text(json.dumps(dict(original, **{field: value})))
                with self.assertRaisesRegex(ValueError, 'exact resolved predecessor'):
                    self.verify()
        for field, value in (('consumerReleaseCount', 0), ('consumerReleaseCount', 3),
                             ('priorPackage', None), ('priorReleaseTag', 'v0.8.0')):
            receipt = json.loads(json.dumps(original))
            receipt['predecessor'][field] = value
            self.consumer_receipt.write_text(json.dumps(receipt))
            with self.assertRaises(ValueError):
                self.verify()

    def test_consumer_n_minus_one_must_restore_current_before_uninstall(self):
        original = self.n_minus_one_consumer_receipt()
        for step in original['steps']:
            step['status'] = 'passed'
        for required in ('restore-current', 'launch-after-restore'):
            for change in ('missing', 'failed', 'not-applicable'):
                with self.subTest(required=required, change=change):
                    receipt = json.loads(json.dumps(original))
                    row = next(step for step in receipt['steps'] if step['name'] == required)
                    if change == 'missing':
                        receipt['steps'].remove(row)
                    else:
                        row['status'] = change
                    self.consumer_receipt.write_text(json.dumps(receipt))
                    with self.assertRaises(ValueError):
                        self.verify()

    def test_promoted_evidence_retains_consumer_receipt_and_resolved_predecessor(self):
        output = self.root / 'publish'
        environment = {'RELEASE_NEEDS': json.dumps(self.needs), 'GITHUB_SHA': 'abc',
                       'GITHUB_RUN_ID': '42', 'GITHUB_RUN_ATTEMPT': '2', 'GITHUB_REPOSITORY': 'test/repo'}
        args = ['verify-release-promotion.py', '--root', str(self.root), '--receipts', str(self.receipts),
                '--smoke', str(self.smoke), '--output', str(output)]
        with patch.dict(os.environ, environment), patch('sys.argv', args):
            self.gate.main()
        evidence = json.loads((output / 'release-gate-evidence.json').read_text())
        consumer = next(receipt for receipt in evidence['lifecycle'] if receipt.get('project') == 'consumer-setup')
        self.assertEqual(consumer, json.loads(self.consumer_receipt.read_text()))
        self.assertEqual(self.gate.digest(output / 'Meridian-Setup.exe'), consumer['currentPackage']['sha256'])

    def test_consumer_receipt_rejects_malformed_evidence_and_unexpected_exceptions(self):
        original = json.loads(self.consumer_receipt.read_text())
        for receipt in ([], None, dict(original, steps=None), dict(original, steps=[None]),
                        dict(original, predecessor=None),
                        dict(original, steps=original['steps'] + [dict(name='optional', status='not-applicable', detail='skip')])):
            with self.subTest(receipt=receipt):
                self.consumer_receipt.write_text(json.dumps(receipt))
                with self.assertRaises(ValueError):
                    self.verify()
        self.consumer_receipt.write_text('{invalid json')
        with self.assertRaisesRegex(ValueError, 'invalid consumer lifecycle'):
            self.verify()

    def test_each_failed_cancelled_skipped_or_missing_dependency_blocks_publication(self):
        for name in self.gate.GATES:
            for state in ('failure', 'cancelled', 'skipped', None):
                with self.subTest(name=name, state=state):
                    self.needs[name]['result'] = state
                    with self.assertRaises(ValueError): self.verify()
            self.needs[name]['result'] = 'success'

    def test_changed_package_bytes_fail_before_promotion(self):
        next(self.root.rglob('*.msix')).write_bytes(b'uncertified change')
        with self.assertRaisesRegex(ValueError, 'digest changed'): self.verify()

    def test_mismatched_source_or_attempt_is_rejected(self):
        manifest = self.manifests[0]
        original = json.loads(manifest.read_text())
        for field in ('commitSha', 'workflowRunId', 'workflowRunAttempt'):
            manifest.write_text(json.dumps(dict(original, **{field: 'wrong'})))
            with self.assertRaisesRegex(ValueError, 'mismatched'): self.verify()

    def test_failed_or_different_certified_package_is_rejected(self):
        file = next(self.receipts.glob('*.json'))
        original = json.loads(file.read_text())
        for value in [dict(original, status='failed'), dict(original, sourceCommit='wrong'), dict(original, currentPackage=dict(original['currentPackage'], sha256='changed'))]:
            file.write_text(json.dumps(value))
            with self.assertRaises(ValueError): self.verify()

    def test_missing_installed_startup_cannot_be_called_publish_smoke_success(self):
        self.smoke.write_text(json.dumps(dict(commitSha='abc', workflowRunId='42', workflowRunAttempt='2', project='web-workstation', runtime='win-x64', validationLanes=['publish-smoke'])))
        with self.assertRaisesRegex(ValueError, 'installed-startup'): self.verify()

    def test_flat_asset_name_collisions_are_rejected(self):
        desktop = [p for p in self.manifests if 'desktop-installer' in p.name]
        # Add the same otherwise valid top-level SBOM to two distinct runtime directories.
        for file in desktop:
            collision = file.parent / 'shared.spdx.json'
            collision.write_text('{}')
            manifest = json.loads(file.read_text())
            manifest['files'].append(dict(path=collision.as_posix(), sha256=self.gate.digest(collision)))
            file.write_text(json.dumps(manifest))
            checksums = next(file.parent.glob('*SHA256SUMS'))
            checksums.write_text('\n'.join(f'{self.gate.digest(p)}  {p.name}' for p in file.parent.iterdir() if p != checksums))
        with self.assertRaisesRegex(ValueError, 'Duplicate release asset'): self.verify()

    def test_rc_to_stable_collision_and_nonincreasing_versions_fail_early(self):
        rc = self.preflight.package_version('1.2.3-rc.1')
        with self.assertRaisesRegex(ValueError, 'RC and stable'):
            self.preflight.require_increasing(self.preflight.package_version('1.2.3'), [rc])
        with self.assertRaises(ValueError): self.preflight.require_increasing((1, 2, 2, 0), [rc])
        self.preflight.require_increasing((1, 2, 4, 0), [rc])
        with self.assertRaises(ValueError): self.preflight.package_version('1.2.3; echo unsafe')
        with self.assertRaises(ValueError): self.preflight.package_version('1.65536.0')

    def test_preflight_reads_actual_msix_identity_and_rejects_mismatched_checkout(self):
        package = self.root / 'test.msix'
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr('AppxManifest.xml', '<Package><Identity Version="5.6.7.8"/></Package>')
        self.assertEqual(self.preflight.msix_version(package), '5.6.7.8')
        with patch.object(self.preflight.subprocess, 'check_output', return_value='wrong\n'):
            with self.assertRaisesRegex(ValueError, 'commit'): self.preflight.verify_source('abc', 'refs/tags/v1.0.0', True)

    def test_release_dependency_graph_and_signing_preconditions_cannot_be_bypassed_by_rehearsal(self):
        workflow = yaml.load((ROOT / '.github/workflows/desktop-installer-packaging.yml').read_text(encoding='utf-8'), Loader=yaml.BaseLoader)
        jobs = workflow['jobs']
        self.assertEqual(workflow['concurrency']['group'], 'desktop-installer-release')
        self.assertEqual(workflow['concurrency']['cancel-in-progress'], 'false')
        self.assertEqual(set(jobs['release']['needs']), self.gate.GATES)
        self.assertNotIn('if', jobs['release'])
        publish = jobs['release']['steps'][-1]
        self.assertIn("github.event_name == 'push'", publish['if'])
        self.assertNotIn('if', jobs['certify-installed-desktop'])
        self.assertNotIn('if', jobs['certify-installed-consumer'])
        self.assertEqual(jobs['certify-installed-consumer']['runs-on'], 'windows-latest')
        self.assertEqual(set(jobs['certify-installed-consumer']['needs']), {'eligibility', 'build-consumer-setup'})
        signing = next(s for s in jobs['eligibility']['steps'] if s.get('name') == 'Validate signing prerequisites before builds')
        self.assertIn('IsNullOrWhiteSpace($env:SIGNING_PFX)', signing['run'])
        self.assertIn('IsNullOrWhiteSpace($env:SIGNING_PASSWORD)', signing['run'])
        for job in ('ci', 'secrets', 'codeql', 'production', 'smoke', 'release-preflight'):
            self.assertEqual(jobs[job]['needs'], 'eligibility')
        self.assertEqual(jobs['smoke']['with']['project'], 'web-workstation')
        self.assertEqual(jobs['smoke']['with']['runtime'], 'win-x64')
