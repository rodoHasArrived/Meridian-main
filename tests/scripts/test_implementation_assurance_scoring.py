"""Regression coverage for evidence-backed assurance rubric verdicts."""

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / '.codex/skills/meridian-implementation-assurance/scripts/score_eval.py'
spec = importlib.util.spec_from_file_location('implementation_assurance_scoring', SCRIPT)
assert spec is not None and spec.loader is not None
scoring = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = scoring
spec.loader.exec_module(scoring)


class AssuranceScoringTests(unittest.TestCase):
    def setUp(self):
        self.scores = dict.fromkeys(scoring.CATEGORIES, 2)
        self.evidence = {key: f'Observed evidence for {key}' for key in scoring.CATEGORIES}

    def result(self, scores=None, failed=None, evidence=None):
        return scoring.EvalResult(
            'A', self.scores if scores is None else scores, failed or [], [],
            self.evidence if evidence is None else evidence,
        )

    def cli(self, scores=None, evidence=None, extra=()):
        args = [sys.executable, str(SCRIPT), '--scenario', 'A', '--json',
                '--scores', json.dumps(self.scores if scores is None else scores)]
        for key, value in (self.evidence if evidence is None else evidence).items():
            args.extend(['--evidence', f'{key}={value}'])
        return subprocess.run(args + list(extra), capture_output=True, text=True, cwd=ROOT)

    def test_complete_evidence_and_scores_pass(self):
        completed = self.cli()
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertTrue(json.loads(completed.stdout)['overall_pass'])

    def test_explicit_failure_overrides_perfect_score_in_both_formats(self):
        result = self.result(failed=['Required integration failed'])
        self.assertEqual(10, result.total)
        self.assertFalse(scoring.to_schema_json(result)['overall_pass'])
        self.assertIn('- Outcome: Fail', scoring.to_markdown(result))
        completed = self.cli(extra=['--failed-check', 'Required integration failed'])
        self.assertEqual(1, completed.returncode)
        self.assertFalse(json.loads(completed.stdout)['overall_pass'])

    def test_missing_evidence_never_passes(self):
        self.assertEqual('Fail', self.result(evidence={}).outcome)
        completed = self.cli(evidence={})
        self.assertEqual(2, completed.returncode)
        self.assertIn('required for every category', completed.stderr)

    def test_zero_or_low_total_fails_with_exit_one(self):
        for values in ([0, 2, 2, 2, 2], [1, 1, 1, 2, 2]):
            with self.subTest(values=values):
                completed = self.cli(scores=dict(zip(scoring.CATEGORIES, values)))
                self.assertEqual(1, completed.returncode)
                self.assertFalse(json.loads(completed.stdout)['overall_pass'])

    def test_threshold_eight_passes_without_zero(self):
        scores = dict(zip(scoring.CATEGORIES, [1, 1, 2, 2, 2]))
        self.assertEqual('Pass', self.result(scores=scores).outcome)

    def test_scores_must_be_actual_json_integers(self):
        for value in (True, False, 1.9, 2.0, '2', None, [], {}):
            with self.subTest(value=value):
                scores = dict(self.scores, behavior_correctness=value)
                with self.assertRaises(ValueError):
                    scoring.validate_scores(scores)
        completed = self.cli(scores=dict(self.scores, behavior_correctness=1.9))
        self.assertEqual(2, completed.returncode)

    def test_score_range_and_category_keys_are_enforced(self):
        for value in (-1, 3):
            with self.assertRaises(ValueError):
                scoring.validate_scores(dict(self.scores, behavior_correctness=value))
        for scores in ({}, dict(self.scores, invented=2)):
            with self.assertRaises(ValueError):
                scoring.validate_scores(scores)

    def test_evidence_is_complete_named_unique_and_nonblank(self):
        good = [f'{key}={value}' for key, value in self.evidence.items()]
        self.assertEqual(self.evidence, scoring.parse_evidence(good))
        for invalid in (['bad'], ['unknown=evidence'], good[:-1],
                        good + [good[0]], good[1:] + ['behavior_correctness=  ']):
            with self.subTest(invalid=invalid):
                with self.assertRaises(ValueError):
                    scoring.parse_evidence(invalid)

    def test_non_object_scores_are_invalid(self):
        completed = subprocess.run(
            [sys.executable, str(SCRIPT), '--scenario', 'A', '--scores', '[]'],
            capture_output=True, text=True, cwd=ROOT,
        )
        self.assertEqual(2, completed.returncode)
        self.assertIn('JSON object', completed.stderr)


if __name__ == '__main__':
    unittest.main()
