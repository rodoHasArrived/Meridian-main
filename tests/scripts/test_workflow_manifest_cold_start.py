"""Ignored reports must pass artifact governance on their first generation."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


manifest = load('cold_workflow_manifest', 'build/scripts/docs/generate-workflow-manifest.py')
automation = load('cold_workflow_automation', 'build/scripts/docs/run-docs-automation.py')


class WorkflowManifestColdStartTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.outputs = ['docs/status/workflow-validation-summary.json',
                        'docs/status/workflow-drift-report.md',
                        'docs/generated/workflow-command-reference.md', 'docs/HELP.md',
                        'docs/development/documentation-automation.md']
        self.outer_outputs = ['docs/status/docs-automation-summary.md',
                              'docs/status/docs-automation-summary.json']
        self.required = 'docs/status/required-upstream.json'
        self.workflow = {
            'id': 'docs-automation-core', 'owners': ['fixture'],
            'requiredPrerequisites': [], 'executionCommands': ['python3 --version'],
            'expectedArtifacts': self.outputs + self.outer_outputs + [self.required],
            'validationChecks': [], 'artifactPolicy': {
                'ownerLane': 'fixture', 'refreshTrigger': 'fixture',
                'retention': {'policy': 'replace-in-place', 'maxAgeDays': 30, 'retainLatest': 1},
                'canonicalOutputRoots': ['docs'],
                'driftChecks': {'enforceMissing': True, 'enforceFreshness': True,
                                'enforceCanonicalPaths': True},
            },
        }
        self.write('docs/status/workflow-manifest.json', json.dumps({'workflows': [self.workflow]}))
        self.write('docs/HELP.md', '# Help\n')
        self.write('docs/development/documentation-automation.md', '# Automation\n')
        self.write(self.required, '{}\n')
        self.manifest_args = argparse.Namespace(
            manifest='docs/status/workflow-manifest.json', help_doc='docs/HELP.md',
            dev_doc='docs/development/documentation-automation.md',
            commands_output='docs/generated/workflow-command-reference.md',
            summary_output='docs/status/workflow-validation-summary.json',
            drift_output='docs/status/workflow-drift-report.md')

    def write(self, name, content):
        target = self.root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding='utf-8')

    def run_manifest(self):
        fake_file = str(self.root / 'build/scripts/docs/generate-workflow-manifest.py')
        with patch.object(manifest, '__file__', fake_file), patch.object(manifest, 'parse_args', return_value=self.manifest_args):
            return manifest.main()

    def test_first_invocation_generates_own_reports_before_final_audit(self):
        for path in self.outer_outputs:
            self.write(path, '{}\n')
        self.assertFalse((self.root / self.outputs[0]).exists())
        self.assertFalse((self.root / self.outputs[1]).exists())
        self.assertEqual(0, self.run_manifest())
        report = json.loads((self.root / self.outputs[0]).read_text())
        self.assertEqual('clean', report['status'])
        self.assertFalse(any(report['governance'].values()))

    def test_real_missing_upstream_artifact_still_fails(self):
        for path in self.outer_outputs:
            self.write(path, '{}\n')
        (self.root / self.required).unlink()
        self.assertEqual(1, self.run_manifest())
        report = json.loads((self.root / self.outputs[0]).read_text())
        self.assertEqual(['docs-automation-core:' + self.required], report['governance']['missingArtifacts'])
        self.assertEqual('drift-detected', report['status'])

    def test_outer_automation_emits_partial_summary_before_manifest_then_finalizes(self):
        args = argparse.Namespace(dry_run=False, profile='core', auto_create_todos=False,
                                  continue_on_error=False, summary_output=str(self.root / self.outer_outputs[0]),
                                  json_output=str(self.root / self.outer_outputs[1]))
        def child(name, root, extra_args):
            self.assertEqual('generate-workflow-manifest', name)
            partial = json.loads((self.root / self.outer_outputs[1]).read_text())
            self.assertTrue(partial['in_progress'])
            self.assertEqual([], partial['results'])
            self.assertIn('in progress', (self.root / self.outer_outputs[0]).read_text())
            result = self.run_manifest()
            return automation.ScriptResult(name=name, command=['fixture'], return_code=result,
                                           duration_seconds=0.0, status='success' if result == 0 else 'failed')
        with patch.object(automation, '__file__', str(self.root / 'build/scripts/docs/run-docs-automation.py')), \
             patch.object(automation, 'parse_args', return_value=args), \
             patch.object(automation, 'resolve_selected_scripts', return_value=['generate-workflow-manifest']), \
             patch.object(automation, 'run_script_with_args', side_effect=child):
            self.assertEqual(0, automation.main())
        final = json.loads((self.root / self.outer_outputs[1]).read_text())
        self.assertNotIn('in_progress', final)
        self.assertEqual('success', final['results'][0]['status'])

    def test_outer_automation_retains_a_required_child_failure(self):
        args = argparse.Namespace(dry_run=False, profile='core', auto_create_todos=False,
                                  continue_on_error=False, summary_output=str(self.root / self.outer_outputs[0]),
                                  json_output=str(self.root / self.outer_outputs[1]))
        failure = automation.ScriptResult(name='generate-workflow-manifest', command=['fixture'],
                                          return_code=1, duration_seconds=0.0, status='failed',
                                          error='missing governed source artifact')
        with patch.object(automation, '__file__', str(self.root / 'build/scripts/docs/run-docs-automation.py')), \
             patch.object(automation, 'parse_args', return_value=args), \
             patch.object(automation, 'resolve_selected_scripts', return_value=['generate-workflow-manifest']), \
             patch.object(automation, 'run_script_with_args', return_value=failure):
            self.assertEqual(1, automation.main())
        final = json.loads((self.root / self.outer_outputs[1]).read_text())
        self.assertNotIn('in_progress', final)
        self.assertEqual('failed', final['results'][0]['status'])
        self.assertEqual(1, final['results'][0]['return_code'])
