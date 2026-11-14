"""Fail-closed contract checks for the structured-status major migration."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import yaml

from tests.scripts.test_roadmap_source_docs import (
    ROOT, _registry_root, load_script, validate_roadmap,
)

renderer = load_script('structured_roadmap_renderer', ROOT / 'build/scripts/docs/render-roadmap-docs.py')
summary = load_script('structured_program_summary', ROOT / 'scripts/generate_program_state_summary.py')


class StructuredRoadmapStatusTests(unittest.TestCase):
    def errors_after(self, mutate, registry='roadmap-items.yml'):
        with tempfile.TemporaryDirectory() as temp:
            root = _registry_root(temp, None)
            path = root / 'docs/roadmap/data' / registry
            data = yaml.safe_load(path.read_text())
            mutate(data)
            path.write_text(yaml.safe_dump(data, sort_keys=False))
            return [f.message for f in validate_roadmap.validate(root) if f.severity == 'error']

    def test_caps_are_enforced_by_the_ci_validator(self):
        for field, value in [('status_note', 'x' * 321), ('title', 'x' * 181), ('exit_criteria', ['x' * 1001])]:
            with self.subTest(field=field):
                errors = self.errors_after(lambda d: d['items'][0].update({field: value}))
                self.assertTrue(any('too long' in error for error in errors), errors)

    def test_structured_references_and_removed_prose_are_checked(self):
        cases = [
            ({'decision_ids': ['DEC-NO-SUCH-DECISION']}, 'unknown decision'),
            ({'pr_references': [True]}, 'not of type'),
            ({'current_summary': 'Unbounded status mirror'}, 'Additional properties'),
            ({'status_detail': '../outside-repository.md'}, 'existing repository file'),
        ]
        for fields, expected in cases:
            with self.subTest(fields=fields):
                errors = self.errors_after(lambda d: d['items'][0].update(fields))
                self.assertTrue(any(expected in error for error in errors), errors)

    def test_migration_snapshots_remain_linked_without_requiring_new_prose_logs(self):
        data = yaml.safe_load((ROOT / 'docs/roadmap/data/roadmap-items.yml').read_text())
        by_id = {item['id']: item for item in data['items']}
        for snapshot in (ROOT / 'docs/roadmap/snapshots/status-prose-2026-11-14').glob('W*.md'):
            if snapshot.stem in by_id:
                self.assertEqual(snapshot.relative_to(ROOT).as_posix(), by_id[snapshot.stem]['status_detail'])
        errors = self.errors_after(lambda d: d['items'][0].pop('status_detail'))
        self.assertEqual([], errors)  # New planned work may use existing evidence without a prose log.

    def test_scope_ids_and_queue_are_references_not_status_mirrors(self):
        errors = self.errors_after(
            lambda d: d['program']['active_scope'].update(work_queue=['W99-ABSENT-001']),
            'program-state.yml',
        )
        self.assertTrue(any('unknown roadmap item' in error for error in errors), errors)
        errors = self.errors_after(lambda d: d['program'].update(active_scope='Old prose'), 'program-state.yml')
        self.assertTrue(any('not of type' in error for error in errors), errors)

    def test_unknown_major_fails_and_v1_compatibility_remains_explicit(self):
        errors = self.errors_after(lambda d: d['schema'].update(version='9.0.0'))
        self.assertTrue(any('unsupported schema major' in error for error in errors), errors)
        header = {'schema': {'id': 'meridian.roadmap-items', 'version': '1.1.0'}}
        self.assertEqual([], validate_roadmap.validate_schema_header(Path('legacy.yml'), header, 'meridian.roadmap-items'))

    def test_generated_register_follows_status_and_retains_boundaries(self):
        program = {'program': {'snapshot_date': '2026-11-14'}}
        item = {'id': 'W1-TEST-001', 'status': 'accepted', 'health': 'yellow', 'evidence_posture': 'partial',
                'status_note': 'Independent review remains open.', 'decision_ids': ['DEC-TEST-001'],
                'pr_references': [123], 'status_detail': 'docs/roadmap/snapshots/test.md'}
        text = renderer.render_register(ROOT, program, {'items': [item]}, [])
        self.assertIn('accepted; health yellow; evidence partial', text)
        self.assertIn('Independent review remains open.', text)
        self.assertIn('DEC-TEST-001', text)
        self.assertIn('/pull/123', text)
        self.assertIn('../snapshots/test.md', text)
        item['status'] = 'in_progress'
        self.assertIn('in_progress; health yellow', renderer.render_register(ROOT, program, {'items': [item]}, []))

    def test_scope_narrative_is_computed_from_current_item_status(self):
        program = {'active_scope': {'roadmap_items': ['W1-TEST-001'], 'decision_ids': [], 'work_queue': []}}
        row = {key: '' for key in ['ID', 'Wave', 'Title', 'Workspaces', 'Status', 'Health', 'Priority', 'Owner Lane', 'Evidence Posture', 'Last Reviewed']}
        row.update(ID='W1-TEST-001', Status='planned')
        self.assertIn('planned: 1', summary.render_markdown(program, [row]))
        row['Status'] = 'accepted'
        text = summary.render_markdown(program, [row])
        self.assertIn('accepted: 1', text)
        self.assertNotIn('planned: 1', text)


if __name__ == '__main__':
    unittest.main()
