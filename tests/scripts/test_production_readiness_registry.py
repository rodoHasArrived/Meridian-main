"""Migration preservation and fail-closed production-readiness rendering coverage."""
from __future__ import annotations

import copy
import importlib.util
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'build/scripts/docs'))
from common import load_data

SPEC = importlib.util.spec_from_file_location('readiness_renderer', ROOT / 'build/scripts/docs/render-production-readiness.py')
assert SPEC and SPEC.loader
renderer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(renderer)


class ProductionReadinessRegistryTests(unittest.TestCase):
    def mutate(self, change, *, evidence_files=()):
        data = copy.deepcopy(load_data(ROOT / renderer.DATA))
        # Fixture owns only the migration snapshot; external evidence is validated in the
        # repository test below, so mutation cases don't copy unrelated product documentation.
        for item in data['items']:
            item['evidence_links'] = []
        change(data)
        import yaml
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root = Path(temp)
            path = root / renderer.DATA
            path.parent.mkdir(parents=True)
            path.write_text(yaml.safe_dump(data, sort_keys=False), encoding='utf-8')
            snapshot = root / data['registry']['migration_snapshot']
            snapshot.parent.mkdir(parents=True)
            snapshot.write_text('Retained migration evidence.\n', encoding='utf-8')
            for relative in evidence_files:
                evidence = root / relative
                evidence.parent.mkdir(parents=True, exist_ok=True)
                evidence.write_text('Retained evidence.\n', encoding='utf-8')
            return renderer.validate_data(root)

    def test_current_registry_and_generated_tracker_are_valid(self):
        self.assertEqual([], renderer.validate_data(ROOT))
        result = subprocess.run([sys.executable, str(ROOT / 'build/scripts/docs/render-production-readiness.py'), '--root', str(ROOT), '--check'], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_every_backlog_row_and_anchor_survives_the_migration(self):
        data = load_data(ROOT / renderer.DATA)
        snapshot = (ROOT / data['registry']['migration_snapshot']).read_text(encoding='utf-8')
        backlog = snapshot.split('## Master Backlog', 1)[1].split('## Duplicate and Deprecation Register', 1)[0]
        old_ids = re.findall(r'^\| `(PRD-\d{3})` \|', backlog, re.MULTILINE)
        self.assertEqual(43, len(old_ids))
        self.assertEqual(set(old_ids), {item['id'] for item in data['items']})
        tracker = renderer.render(ROOT)
        for line in snapshot.splitlines():
            if re.match(r'^#{2,3} ', line):
                self.assertIn('\n' + line + '\n', tracker)
        # Important reservations must remain explicit in the fast context and complete detail.
        self.assertIn('same release commit', tracker)
        self.assertIn('All eight workflows', snapshot)
        self.assertLess(len(tracker.encode()), 45000)

    def test_implementation_grades_do_not_become_release_certification(self):
        data = load_data(ROOT / renderer.DATA)
        by_id = {item['id']: item for item in data['items']}
        self.assertEqual('implementation_complete', by_id['PRD-003']['status'])
        self.assertEqual('implementation_complete', by_id['PRD-103']['status'])
        self.assertEqual('implementation_complete', by_id['PRD-109']['status'])
        self.assertEqual('in_progress', by_id['PRD-002']['status'])
        self.assertEqual('blocked', data['registry']['release_status'])
        self.assertTrue(all(item['release_status'] == 'open' for item in data['items']))
        self.assertEqual([3380], by_id['PRD-115']['pr_references'])

    def test_false_release_ready_and_unsupported_major_fail_closed(self):
        errors = self.mutate(lambda data: data['registry'].update(release_status='ready'))
        self.assertTrue(any('every P0 row' in error for error in errors), errors)
        errors = self.mutate(lambda data: data['schema'].update(version='2.0.0'))
        self.assertTrue(any('unsupported' in error for error in errors), errors)

    def test_release_closure_requires_commit_and_evidence(self):
        errors = self.mutate(lambda data: data['items'][0].update(release_status='complete'))
        self.assertTrue(any('frozen release_commit' in error for error in errors), errors)
        self.assertTrue(any('requires evidence_links' in error for error in errors), errors)

    def test_unbounded_prose_duplicate_ids_and_unknown_fields_are_rejected(self):
        def mutate(data):
            data['items'][0]['remaining_gate'] = 'x' * 321
            data['items'][1]['id'] = data['items'][0]['id']
            data['items'][0]['narrative_history'] = 'unexpected'
        errors = self.mutate(mutate)
        for fragment in ('1-320 characters', 'duplicate ID', 'unexpected or missing fields'):
            self.assertTrue(any(fragment in error for error in errors), errors)

    def test_invalid_pr_and_unsafe_evidence_links_are_rejected(self):
        def mutate(data):
            data['items'][0]['pr_references'] = [True]
            data['items'][0]['evidence_links'] = ['javascript:alert(1)']
        errors = self.mutate(mutate)
        self.assertTrue(any('invalid pr_references' in error for error in errors), errors)
        self.assertTrue(any('invalid evidence URL' in error for error in errors), errors)

    def test_rendering_is_deterministic(self):
        self.assertEqual(renderer.render(ROOT), renderer.render(ROOT))

    def test_archived_evidence_is_supported_and_local_paths_stay_in_repository(self):
        def archived(data):
            data['items'][0]['evidence_links'] = ['archive/docs/receipt.md']
        self.assertEqual([], self.mutate(archived, evidence_files=['archive/docs/receipt.md']))
        def outside(data):
            data['items'][0]['evidence_links'] = ['docs/../../../outside.md']
        errors = self.mutate(outside)
        self.assertTrue(any('outside repository' in error for error in errors), errors)

    def test_renaming_priority_headings_cannot_silently_hide_backlog_rows(self):
        def mutate(data):
            for section in data['detail_sections']:
                if section['heading'] == 'P0 — Release Blockers':
                    section['heading'] = 'Release concerns'
        errors = self.mutate(mutate)
        self.assertTrue(any('required priority section' in error for error in errors), errors)


if __name__ == '__main__':
    unittest.main()
