"""Execute MSIX predecessor selection with controlled release history and downloads."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]


class MsixPredecessorWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.pwsh = shutil.which('pwsh')
        self.assertIsNotNone(self.pwsh, 'PowerShell 7 is required for MSIX predecessor tests.')
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        workflow = yaml.load((ROOT / '.github/workflows/desktop-installer-packaging.yml').read_text(),
                             Loader=yaml.BaseLoader)
        steps = workflow['jobs']['certify-installed-desktop']['steps']
        step = next(step for step in steps if step.get('id') == 'prior')
        script = step['run'].replace('${{ matrix.runtime }}', 'win-x64').replace('${{ github.repository }}', 'owner/repo')
        self.script = self.root / 'step.ps1'
        self.script.write_text(script)
        (self.root / 'artifacts/current').mkdir(parents=True)
        (self.root / 'artifacts/current/current-win-x64.msix').write_bytes(b'current package')
        self.history = []
        self.environment = dict(os.environ, CURRENT_RELEASE_TAG='v0.1.0', CONFIGURED_PRIOR_TAG='',
                                GITHUB_OUTPUT=str(self.root / 'outputs'),
                                HISTORY=str(self.root / 'history.json'), CALLS=str(self.root / 'calls.jsonl'))
        tools = self.root / 'bin'
        tools.mkdir()
        self.environment['PATH'] = str(tools) + os.pathsep + os.environ.get('PATH', '')
        gh = tools / 'gh'
        gh.write_text(
            '#!/usr/bin/env python3\n'
            'import json, os, pathlib, sys\n'
            'with pathlib.Path(os.environ["CALLS"]).open("a") as out: out.write(json.dumps(sys.argv[1:]) + "\\n")\n'
            'if sys.argv[1] == "api":\n'
            '    if os.environ.get("FAIL_QUERY"): sys.exit(17)\n'
            '    print(pathlib.Path(os.environ["HISTORY"]).read_text())\n'
            'elif sys.argv[1:3] == ["release", "download"]:\n'
            '    if os.environ.get("FAIL_DOWNLOAD"): sys.exit(19)\n'
            '    path = pathlib.Path("artifacts/prior/prior-win-x64.msix")\n'
            '    path.write_bytes(b"prior signed package")\n'
            'else: sys.exit(21)\n'
        )
        gh.chmod(0o755)

    def release(self, tag, date='2026-10-21T00:00:00Z', *, draft=False, prerelease=False):
        return dict(tag_name=tag, published_at=date, draft=draft, prerelease=prerelease)

    def run_step(self):
        (self.root / 'history.json').write_text(json.dumps(self.history))
        (self.root / 'outputs').unlink(missing_ok=True)
        return subprocess.run([self.pwsh, '-NoProfile', '-File', str(self.script)], cwd=self.root,
                              env=self.environment, capture_output=True, text=True, timeout=30)

    def outputs(self):
        return (self.root / 'outputs').read_text()

    def test_stable_uses_production_rc_and_excludes_current_evaluation_drafts(self):
        self.history = [[self.release('v0.1.0-rc.1', '2026-10-20T00:00:00Z', prerelease=True)],
                        [self.release('v0.1.0-rc.2', prerelease=True),
                         self.release('eval-v0.1.0', '2026-10-24T00:00:00Z', prerelease=True),
                         self.release('v0.1.0-rc.3', '2026-10-25T00:00:00Z', draft=True),
                         self.release('v0.1.0', '2026-10-26T00:00:00Z')]]
        result = self.run_step()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('prior_tag=v0.1.0-rc.2', self.outputs())
        self.assertIn('first_release=false', self.outputs())
        calls = [json.loads(line) for line in (self.root / 'calls.jsonl').read_text().splitlines()]
        self.assertEqual(calls[-1][0:3], ['release', 'download', 'v0.1.0-rc.2'])

    def test_no_production_predecessor_records_explicit_first_release(self):
        self.history = [[self.release('eval-v0.1.0', prerelease=True),
                         self.release('v0.1.0-rc.1', draft=True), self.release('v0.1.0')]]
        result = self.run_step()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('first_release=true', self.outputs())

    def test_configured_predecessor_cannot_be_current_evaluation_draft_or_n_minus_two(self):
        self.history = [[self.release('v0.1.0-rc.1', '2026-10-20T00:00:00Z', prerelease=True),
                         self.release('v0.1.0-rc.2', prerelease=True),
                         self.release('v0.1.0-rc.3', draft=True), self.release('eval-v0.1.0'),
                         self.release('v0.1.0')]]
        for tag in ('v0.1.0', 'eval-v0.1.0', 'v0.1.0-rc.3', 'v0.1.0-rc.1'):
            with self.subTest(tag=tag):
                self.environment['CONFIGURED_PRIOR_TAG'] = tag
                result = self.run_step()
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn('first_release=true', self.outputs())
        self.environment['CONFIGURED_PRIOR_TAG'] = 'v0.1.0-rc.2'
        self.assertEqual(self.run_step().returncode, 0)

    def test_query_download_and_malformed_history_fail_closed(self):
        self.history = [[self.release('v0.1.0-rc.2', prerelease=True)]]
        for failure in ('FAIL_QUERY', 'FAIL_DOWNLOAD'):
            with self.subTest(failure=failure):
                self.environment[failure] = '1'
                result = self.run_step()
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn('first_release=true', self.outputs())
                del self.environment[failure]
        for history in ({'error': 'bad response'}, [{'tag_name': 'v0.1.0-rc.1'}], [[{'tag_name': 'v0.1.0-rc.1'}]]):
            self.history = history
            result = self.run_step()
            self.assertNotEqual(result.returncode, 0)
            self.assertNotIn('first_release=true', self.outputs())


if __name__ == '__main__':
    unittest.main()
