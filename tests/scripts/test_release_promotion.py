import importlib.util
import json
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
        self.smoke = self.root / 'startup.json'
        self.smoke.write_text(json.dumps(dict(commitSha='abc', workflowRunId='42', workflowRunAttempt='2', project='web-workstation', runtime='win-x64', validationLanes=['web-workstation-installed-startup'])))

    def verify(self):
        return self.gate.verify(self.root, self.receipts, self.smoke, 'abc', '42', '2', self.needs)

    def test_success_promotes_exact_digest_evidence_for_both_architectures_and_consumer(self):
        assets, receipts, startup = self.verify()
        self.assertEqual(len(assets), 12)
        self.assertEqual(len(receipts), 2)
        self.assertIn('Meridian-Setup.exe', assets)
        self.assertEqual(startup['runtime'], 'win-x64')

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
        signing = next(s for s in jobs['eligibility']['steps'] if s.get('name') == 'Validate signing prerequisites before builds')
        self.assertIn('IsNullOrWhiteSpace($env:SIGNING_PFX)', signing['run'])
        self.assertIn('IsNullOrWhiteSpace($env:SIGNING_PASSWORD)', signing['run'])
        for job in ('ci', 'secrets', 'codeql', 'production', 'smoke', 'release-preflight'):
            self.assertEqual(jobs[job]['needs'], 'eligibility')
        self.assertEqual(jobs['smoke']['with']['project'], 'web-workstation')
        self.assertEqual(jobs['smoke']['with']['runtime'], 'win-x64')
