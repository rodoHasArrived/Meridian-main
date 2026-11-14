"""CI output ownership must remove churn without hiding reviewed drift."""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'build/scripts'))
from generated_output_policy import delivery


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


generation = load('delivery_generation', 'build/scripts/run-maintained-generation.py')
checker = load('delivery_checker', 'build/scripts/ci/check-generated-output.py')


class GeneratedDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.policy = {'whole_files': ['docs/status/tracker.json'], 'whole_trees': ['web/build'],
                       'hybrid_files': {}, 'delivery': {'ci_files': ['docs/status/tracker.json'],
                       'ci_trees': ['web/build'], 'default': 'reviewed-commit'}}
        self.write('build/scripts/maintained-generation-outputs.json', json.dumps(self.policy))
        self.write('.gitignore', 'artifacts/\ndocs/status/tracker.json\nweb/build/\n')
        self.write('reviewed.md', 'reviewed\n')
        self.git('init')
        self.git('config', 'user.name', 'Fixture')
        self.git('config', 'user.email', 'fixture@example.invalid')
        self.git('add', '.')
        self.git('commit', '-m', 'fixture')

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, stderr=subprocess.DEVNULL)

    def write(self, path, value):
        destination = self.root / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(value)

    def test_ignored_machine_changes_converge_and_export_without_review_patch(self):
        step = generation.Step('tracker', ('fixture',))
        def produce(root, step):
            self.write('docs/status/tracker.json', '{"source": "current"}\n')
            self.write('web/build/assets/app-new.js', 'new\n')
            return 0
        result = generation.run_generation(self.root, [step], self.policy, runner=produce)
        self.assertTrue(result['successful'])
        self.assertEqual(2, len(result['passes']))
        self.assertIn('docs/status/tracker.json', result['changed_outputs']['added'])
        output = self.root / 'artifacts/docs'
        self.assertTrue(checker.check(self.root, output))
        self.assertEqual(b'', (output / 'generated-repair.patch').read_bytes())
        exported = json.loads((output / 'generated/outputs.json').read_text())
        self.assertEqual(self.git('rev-parse', 'HEAD').decode().strip(), exported['revision'])
        self.assertIn('web/build/assets/app-new.js', exported['files'])
        # Re-export removes obsolete hashed files from evidence.
        (self.root / 'web/build/assets/app-new.js').unlink()
        self.assertTrue(checker.check(self.root, output))
        self.assertFalse((output / 'generated/web/build/assets/app-new.js').exists())
        self.write('reviewed.md', 'stale reviewed snapshot\n')
        self.assertFalse(checker.check(self.root, output))
        drift = json.loads((output / 'drift.json').read_text())
        self.assertEqual(['reviewed.md'], drift['changed_paths'])

    def test_cs_change_needs_no_followup_wpf_tracker_commit(self):
        trackers = ['docs/status/wpf-screen-development-tracker.md',
                    'docs/status/wpf-screen-development-tracker.json']
        self.policy['whole_files'] += trackers
        self.policy['delivery']['ci_files'] += trackers
        self.write('build/scripts/maintained-generation-outputs.json', json.dumps(self.policy))
        self.write('.gitignore', (self.root / '.gitignore').read_text() + '\n'.join(trackers) + '\n')
        self.write('src/Screen.cs', 'class UpdatedScreen {}\n')
        self.git('add', '.')
        self.git('commit', '-m', 'authored screen change')
        revision = self.git('rev-parse', 'HEAD')
        def produce(root, step):
            for path in trackers:
                self.write(path, (root / 'src/Screen.cs').read_text())
            return 0
        result = generation.run_generation(self.root, [generation.Step('tracker', ('fixture',))], self.policy, runner=produce)
        self.assertTrue(result['successful'])
        self.assertTrue(checker.check(self.root, self.root / 'artifacts/docs'))
        self.assertEqual(revision, self.git('rev-parse', 'HEAD'))
        self.assertEqual(b'', self.git('status', '--porcelain'))
        self.assertEqual(b'', (self.root / 'artifacts/docs/generated-repair.patch').read_bytes())

    def test_force_adding_machine_output_is_rejected(self):
        self.write('docs/status/tracker.json', '{}\n')
        self.git('add', '-f', 'docs/status/tracker.json')
        self.assertFalse(checker.check(self.root, self.root / 'artifacts/docs'))
        drift = json.loads((self.root / 'artifacts/docs/drift.json').read_text())
        self.assertEqual(['docs/status/tracker.json'], drift['tracked_ci_outputs'])

    def test_ignored_machine_symlinks_are_rejected(self):
        self.write('secret.txt', 'not an output')
        path = self.root / 'docs/status/tracker.json'
        path.parent.mkdir(parents=True, exist_ok=True)
        path.symlink_to(self.root / 'secret.txt')
        with self.assertRaisesRegex(ValueError, 'Symlink CI output'):
            generation.snapshot(self.root, self.policy)

    def test_delivery_cannot_grant_whole_file_access_to_reviewed_prose(self):
        self.policy['delivery']['ci_files'].append('reviewed.md')
        with self.assertRaisesRegex(ValueError, 'whole-output authority'):
            delivery(self.policy)

    def test_machine_output_symlink_write_restores_invocation_bytes(self):
        self.write('docs/status/tracker.json', 'before\n')
        self.write('human.txt', 'secret\n')
        def produce(root, step):
            path = root / 'docs/status/tracker.json'
            path.unlink()
            path.symlink_to(root / 'human.txt')
            return 0
        result = generation.run_generation(self.root, [generation.Step('bad', ('fixture',))], self.policy, runner=produce)
        self.assertFalse(result['successful'])
        self.assertFalse((self.root / 'docs/status/tracker.json').is_symlink())
        self.assertEqual('before\n', (self.root / 'docs/status/tracker.json').read_text())
        self.assertEqual('secret\n', (self.root / 'human.txt').read_text())

    def test_ci_tree_cannot_encompass_a_reviewed_baseline(self):
        self.policy['delivery']['reviewed_baselines'] = ['web/build/reviewed.json']
        with self.assertRaisesRegex(ValueError, 'reviewed content'):
            delivery(self.policy)

    def test_source_review_fingerprints_do_not_depend_on_generated_mirrors(self):
        sys.path.insert(0, str(ROOT / 'build/scripts/docs'))
        hashes = load('delivery_source_hashes', 'build/scripts/docs/validate-doc-hashes.py')
        self.write('src/UI/authored.ts', 'export const source = true;\n')
        before = hashes.tree_hash(self.root, self.root / 'src/UI')
        self.write('src/UI/ui-api-routes.generated.ts', 'generated route cache\n')
        self.write('src/UI/workspace-catalog.generated.ts', 'generated workspace cache\n')
        self.assertEqual(before, hashes.tree_hash(self.root, self.root / 'src/UI'))
        self.write('src/UI/authored.ts', 'export const source = false;\n')
        self.assertNotEqual(before, hashes.tree_hash(self.root, self.root / 'src/UI'))

    def test_repository_classification_preserves_review_evidence(self):
        policy = json.loads((ROOT / 'build/scripts/maintained-generation-outputs.json').read_text())
        config = delivery(policy)
        self.assertIn('docs/status/wpf-screen-development-tracker.json', config['ci_files'])
        self.assertNotIn('docs/source/generated/source-hash-manifest.json', config['ci_files'])
        self.assertNotIn('docs/generated/database', config['ci_trees'])
        self.assertNotIn('docs/status/coverage-report.md', config['ci_files'])
