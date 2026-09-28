import importlib.util
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('scan_commit_secrets', ROOT / 'build/scripts/ci/scan-commit-secrets.py')
SCANNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SCANNER)


class CommitSecretScanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.report = Path(self.temp.name) / 'result.sarif'
        self.sha = 'a' * 40

    def git_output(self, command, **kwargs):
        return self.sha if command[-1] == 'HEAD' else 'false'

    def test_shallow_checkout_cannot_claim_complete_scan_coverage(self):
        with patch.object(SCANNER.subprocess, 'check_output', side_effect=[self.sha, 'true']), patch.object(SCANNER.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'shallow'): SCANNER.scan('gitleaks', self.sha, self.report)
            run.assert_not_called()

    def test_scans_exact_history_and_merge_changes_without_a_push_payload(self):
        def execute(command, **kwargs):
            self.assertIn(f'--log-opts=--full-history -m {self.sha}', command)
            self.assertIn('--redact', command)
            self.report.write_text(json.dumps({'version': '2.1.0', 'runs': [{'results': []}]}))
            return types.SimpleNamespace(returncode=0)
        with patch.object(SCANNER.subprocess, 'check_output', side_effect=self.git_output), patch.object(SCANNER.subprocess, 'run', side_effect=execute):
            self.assertEqual(SCANNER.scan('gitleaks', self.sha, self.report), 0)

    def test_rejects_mismatched_checkout_before_scanning(self):
        with patch.object(SCANNER.subprocess, 'check_output', return_value='b' * 40), patch.object(SCANNER.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'checkout'): SCANNER.scan('gitleaks', self.sha, self.report)
            run.assert_not_called()

    def test_propagates_leak_and_scanner_failures(self):
        for code in (1, 2):
            with patch.object(SCANNER.subprocess, 'check_output', side_effect=self.git_output), patch.object(SCANNER.subprocess, 'run', return_value=types.SimpleNamespace(returncode=code)):
                self.assertEqual(SCANNER.scan('gitleaks', self.sha, self.report), code)

    def test_old_report_cannot_hide_missing_fresh_evidence(self):
        self.report.write_text('{"version":"2.1.0","runs":[{}]}')
        with patch.object(SCANNER.subprocess, 'check_output', side_effect=self.git_output), patch.object(SCANNER.subprocess, 'run', return_value=types.SimpleNamespace(returncode=0)):
            with self.assertRaises(FileNotFoundError): SCANNER.scan('gitleaks', self.sha, self.report)

    def test_findings_cannot_pass_with_success_exit_code(self):
        def execute(*args, **kwargs):
            self.report.write_text(json.dumps({'version': '2.1.0', 'runs': [{'results': [{'ruleId': 'example'}]}]}))
            return types.SimpleNamespace(returncode=0)
        with patch.object(SCANNER.subprocess, 'check_output', side_effect=self.git_output), patch.object(SCANNER.subprocess, 'run', side_effect=execute):
            with self.assertRaisesRegex(ValueError, 'findings'): SCANNER.scan('gitleaks', self.sha, self.report)

    def test_merge_groups_and_tags_use_the_commit_scanner(self):
        import yaml
        workflow = yaml.load((ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8'), Loader=yaml.BaseLoader)
        self.assertIn('merge_group', workflow['on'])
        steps = workflow['jobs']['secret-scan']['steps']
        scan = next(step for step in steps if 'scan-commit-secrets.py' in step.get('run', ''))
        self.assertIn("github.event_name == 'merge_group'", scan['if'])
        self.assertIn("startsWith(github.ref, 'refs/tags/')", scan['if'])
        self.assertIn('sha256sum -c -', scan['run'])
