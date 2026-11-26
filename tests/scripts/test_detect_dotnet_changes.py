from __future__ import annotations

import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'build/scripts/ci/detect-dotnet-changes.py'
spec = importlib.util.spec_from_file_location('detect_dotnet_changes', SCRIPT)
detector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(detector)


class DotnetChangeDetectionTests(unittest.TestCase):
    def test_only_known_documentation_paths_skip(self):
        paths = ['README.md', 'CLAUDE.md', 'docs/architecture/design.md',
                 'archive/docs/review.md', 'docs/images/diagram.svg',
                 'docs/diagrams/flow.mmd', 'docs/a file\nwith newline.md']
        self.assertFalse(detector.requires_dotnet(paths))
        for path in ['src/README.md', 'tests/README.md', 'src/App.cs', 'src/Core.fs',
                     'global.json', 'Directory.Build.props', 'NuGet.Config',
                     '.github/workflows/meridian-ci.yml', 'scripts/ci.sh',
                     'build/scripts/ci/detect-dotnet-changes.py',
                     'docs/reference/schema.json', 'docs/examples/code.cs',
                     'docs/roadmap/data/items.yml', 'archive/src/Old.cs',
                     'docs/../src/App.cs', 'unknown.txt']:
            with self.subTest(path=path):
                self.assertTrue(detector.requires_dotnet(paths + [path]))
        self.assertTrue(detector.requires_dotnet([]))

    def test_complete_git_diff_handles_renames_deletions_and_large_changes(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=root, text=True).strip()
            git('init', '-q')
            git('config', 'user.email', 'tests@example.invalid')
            git('config', 'user.name', 'Tests')
            (root / 'App.cs').write_text('class App {}')
            git('add', '.')
            git('commit', '-qm', 'base')
            base = git('rev-parse', 'HEAD')
            docs = root / 'docs'
            docs.mkdir()
            (root / 'App.cs').rename(docs / 'App.md')
            for index in range(400):
                (docs / f'{index}.md').write_text('docs')
            (docs / 'a\nnewline.md').write_text('docs')
            git('add', '.')
            git('commit', '-qm', 'rename and docs')
            head = git('rev-parse', 'HEAD')
            output = root / 'output'
            result = subprocess.run(['python3', str(SCRIPT), '--base', base, '--head', head,
                                     '--github-output', str(output)], cwd=root,
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(output.read_text(), 'dotnet=true\n')
            diff = git('diff', '--name-only', '--no-renames', base, head)
            self.assertIn('App.cs', diff)
            (docs / '0.md').unlink()
            git('add', 'docs')
            git('commit', '-qm', 'docs deletion')
            output.unlink()
            result = subprocess.run(['python3', str(SCRIPT), '--base', head,
                                     '--head', git('rev-parse', 'HEAD'),
                                     '--github-output', str(output)], cwd=root,
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(output.read_text(), 'dotnet=false\n')
            output.unlink()
            result = subprocess.run(['python3', str(SCRIPT), '--base', '0' * 40,
                                     '--head', head, '--github-output', str(output)], cwd=root,
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(output.exists(), 'Failed detection must never authorize a skip')

    def test_invalid_boundary_is_rejected(self):
        with self.assertRaises(ValueError):
            detector.changed_paths('--invalid', 'a' * 40)


class QualityGateChangeDetectionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = yaml.load((ROOT / '.github/workflows/meridian-ci.yml').read_text(),
                                 Loader=yaml.BaseLoader)
        cls.jobs = cls.workflow['jobs']

    def run_gate(self, **overrides):
        step = self.jobs['quality-gate']['steps'][0]
        self.assertNotIn('${{', step['run'])
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            env = dict(os.environ, GITHUB_STEP_SUMMARY=str(Path(directory) / 'summary'),
                       DOTNET_LANE_DECISION='true', DOTNET_RESULT='success',
                       BROWSER_RESULT='success', DOCS_RESULT='success', WORKFLOWS_RESULT='success')
            env.update(overrides)
            return subprocess.run(['bash', '-c', step['run']], env=env,
                                  capture_output=True, text=True).returncode

    def test_gate_accepts_only_success_with_a_valid_change_decision(self):
        self.assertEqual(self.run_gate(), 0)
        # A docs-only PR: verify-dotnet runs, skips its .NET steps and reports success.
        self.assertEqual(self.run_gate(DOTNET_LANE_DECISION='false'), 0)
        # The lane can never be skipped as a job, so a skipped verdict always fails.
        self.assertNotEqual(self.run_gate(DOTNET_LANE_DECISION='false', DOTNET_RESULT='skipped'), 0)
        for decision in ['', 'invalid']:
            with self.subTest(decision=decision):
                self.assertNotEqual(self.run_gate(DOTNET_LANE_DECISION=decision), 0)
        # Agreement with the integration lane's independent decision is enforced by
        # integration-gate (see test_service_backed_integrations_workflow).
        for key in ['DOTNET_RESULT', 'BROWSER_RESULT', 'DOCS_RESULT', 'WORKFLOWS_RESULT']:
            for result in ['failure', 'cancelled', 'skipped']:
                with self.subTest(lane=key, result=result):
                    self.assertNotEqual(self.run_gate(**{key: result}), 0)
        self.assertNotEqual(self.run_gate(DOTNET_LANE_DECISION='false', DOTNET_RESULT='failure'), 0)

    def test_workflow_limits_skipping_to_prs_and_keeps_gate_always_reporting(self):
        self.assertEqual(self.jobs['quality-gate']['if'], 'always()')
        self.assertNotIn('changes', self.jobs, 'No lane may queue behind a separate classification job.')
        # verify-dotnet is never skipped as a job; it classifies in-job and skips only
        # its .NET steps.
        self.assertNotIn('needs', self.jobs['verify-dotnet'])
        self.assertNotIn('if', self.jobs['verify-dotnet'])
        lane_checkout, lane_detect = self.jobs['verify-dotnet']['steps'][:2]
        self.assertEqual(lane_checkout['if'], "github.event_name == 'pull_request'")
        self.assertEqual(lane_checkout['with']['fetch-depth'], '0')
        self.assertEqual(lane_checkout['with']['persist-credentials'], 'false')
        self.assertIn('echo "dotnet=true"', lane_detect['run'])
        self.assertIn('if [[ "$EVENT_NAME" == "pull_request" ]]', lane_detect['run'])
        self.assertNotIn('${{', lane_detect['run'])
        for event in ['pull_request', 'push', 'merge_group']:
            config = self.workflow['on'][event]
            if isinstance(config, dict):
                self.assertNotIn('paths', config)
                self.assertNotIn('paths-ignore', config)


if __name__ == '__main__':
    unittest.main()
