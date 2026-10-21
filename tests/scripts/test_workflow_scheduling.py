"""Regression coverage for cancellation isolation and manual dispatch deduplication."""

from __future__ import annotations

import json
import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml


WORKFLOWS = Path(__file__).resolve().parents[2] / ".github" / "workflows"
MANUAL_JOBS = {
    "targeted-test": "targeted",
    "roadmap-tools-manual": "roadmap-tools",
}


def load_workflow(name: str) -> dict:
    # Actions interprets `on` as a key, not a YAML 1.1 boolean.
    return yaml.load((WORKFLOWS / f"{name}.yml").read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class WorkflowSchedulingTests(unittest.TestCase):
    def dispatch_key(self, workflow: dict, inputs: dict, *, ref: str = "refs/heads/main", sha: str = "a" * 40) -> str:
        step = workflow["jobs"]["dispatch-key"]["steps"][0]
        self.assertEqual(step["env"]["DISPATCH_INPUTS"], "${{ toJSON(inputs) }}")
        self.assertEqual(step["env"]["DISPATCH_REF"], "${{ github.ref }}")
        self.assertEqual(step["env"]["DISPATCH_SHA"], "${{ github.sha }}")
        self.assertNotIn("${{", step["run"], "Dispatch values must be passed as data, never shell source.")
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            result = subprocess.run(
                ["bash", "-euo", "pipefail", "-c", step["run"]],
                cwd=directory,
                env=dict(os.environ, DISPATCH_REF=ref, DISPATCH_SHA=sha,
                         DISPATCH_INPUTS=json.dumps(inputs), GITHUB_OUTPUT=str(output)),
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, "", "Raw dispatch inputs must not be logged.")
            self.assertFalse((Path(directory) / "injected").exists())
            match = re.fullmatch(r"group=([0-9a-f]{64})\n", output.read_text(encoding="utf-8"))
            self.assertIsNotNone(match, "Emit one bounded, lowercase digest, even for multiline inputs.")
            return match.group(1)

    def test_ci_separates_event_groups_and_preserves_each_manual_coverage_dispatch(self) -> None:
        concurrency = load_workflow("ci")["concurrency"]
        self.assertEqual(
            concurrency["group"],
            "${{ github.workflow }}-ci-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref }}-${{ github.event_name == 'workflow_dispatch' && github.run_id || 'latest' }}",
        )
        self.assertEqual(
            concurrency["cancel-in-progress"],
            "${{ github.event_name != 'schedule' && github.event_name != 'workflow_dispatch' }}",
        )

    def test_meridian_manual_validation_cannot_be_cancelled_by_push(self) -> None:
        concurrency = load_workflow("meridian-ci")["concurrency"]
        self.assertEqual(
            concurrency["group"],
            "${{ github.workflow }}-meridian-ci-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref }}-${{ github.event_name == 'workflow_dispatch' && github.run_id || 'latest' }}",
        )
        self.assertEqual(
            concurrency["cancel-in-progress"],
            "${{ github.event_name != 'schedule' && github.event_name != 'workflow_dispatch' }}",
        )

    def test_manual_jobs_wait_for_identity_and_preserve_running_validation(self) -> None:
        for name, job_name in MANUAL_JOBS.items():
            with self.subTest(workflow=name):
                workflow = load_workflow(name)
                self.assertNotIn("concurrency", workflow, "Distinct dispatch selections must reach key generation.")
                key_job = workflow["jobs"]["dispatch-key"]
                self.assertEqual(key_job["outputs"]["group"], "${{ steps.key.outputs.group }}")
                self.assertEqual(key_job["steps"][0]["id"], "key")
                job = workflow["jobs"][job_name]
                self.assertEqual(job["needs"], "dispatch-key")
                self.assertEqual(job["concurrency"], {
                    "group": name + "-${{ needs.dispatch-key.outputs.group }}",
                    "cancel-in-progress": "false",
                })

    def test_identical_selections_share_a_key_independent_of_json_property_order(self) -> None:
        for name in MANUAL_JOBS:
            with self.subTest(workflow=name):
                workflow = load_workflow(name)
                inputs = {key: value.get("default", "") for key, value in workflow["on"]["workflow_dispatch"]["inputs"].items()}
                reverse_order = dict(reversed(list(inputs.items())))
                self.assertEqual(self.dispatch_key(workflow, inputs), self.dispatch_key(workflow, reverse_order))

    def test_every_declared_input_ref_and_commit_can_select_independent_work(self) -> None:
        for name in MANUAL_JOBS:
            workflow = load_workflow(name)
            inputs = {key: value.get("default", "") for key, value in workflow["on"]["workflow_dispatch"]["inputs"].items()}
            baseline = self.dispatch_key(workflow, inputs)
            for key in inputs:
                with self.subTest(workflow=name, input=key):
                    changed = dict(inputs, **{key: str(inputs[key]) + "-different"})
                    self.assertNotEqual(baseline, self.dispatch_key(workflow, changed))
            with self.subTest(workflow=name, input="ref"):
                self.assertNotEqual(baseline, self.dispatch_key(workflow, inputs, ref="refs/heads/topic"))
            with self.subTest(workflow=name, input="sha"):
                self.assertNotEqual(baseline, self.dispatch_key(workflow, inputs, sha="b" * 40))

    def test_case_sensitive_filters_paths_and_refs_do_not_alias_in_actions_groups(self) -> None:
        for name, key in (("targeted-test", "dotnet_filter"), ("roadmap-tools-manual", "roadmap_path")):
            with self.subTest(workflow=name):
                workflow = load_workflow(name)
                upper = self.dispatch_key(workflow, {key: "CaseSensitive"}, ref="refs/heads/Topic")
                lower_value = self.dispatch_key(workflow, {key: "casesensitive"}, ref="refs/heads/Topic")
                lower_ref = self.dispatch_key(workflow, {key: "CaseSensitive"}, ref="refs/heads/topic")
                self.assertNotEqual(upper.casefold(), lower_value.casefold())
                self.assertNotEqual(upper.casefold(), lower_ref.casefold())

    def test_long_multiline_and_shell_syntax_inputs_remain_bounded_data(self) -> None:
        hostile = "$(touch injected) `touch injected`\nPY\ntouch injected\n'\"${{ github.token }}"
        for name in MANUAL_JOBS:
            with self.subTest(workflow=name):
                workflow = load_workflow(name)
                self.dispatch_key(workflow, {"selection": hostile + "x" * 50000})
                self.dispatch_key(workflow, {"selection": "plain"}, ref=hostile)

    def test_structured_selections_cannot_collide_through_separator_ambiguity(self) -> None:
        for name in MANUAL_JOBS:
            with self.subTest(workflow=name):
                workflow = load_workflow(name)
                first = self.dispatch_key(workflow, {"first": "a-b", "second": "c"})
                second = self.dispatch_key(workflow, {"first": "a", "second": "b-c"})
                self.assertNotEqual(first, second)

    def test_benchmarks_share_one_global_group_and_keep_full_pair_collection(self) -> None:
        workflow = load_workflow("ci-concurrency-benchmark")
        self.assertEqual(workflow["concurrency"], {
            "group": "ci-concurrency-benchmark", "cancel-in-progress": "false",
        })
        pairs = workflow["jobs"]["pairs"]
        self.assertEqual(pairs["strategy"]["matrix"]["pair"], ["1", "2", "3", "4", "5"])
        self.assertEqual(pairs["strategy"]["fail-fast"], "false")
        self.assertEqual(pairs["strategy"]["max-parallel"], "2")
        self.assertNotIn("concurrency", pairs, "Concurrency belongs to the whole benchmark, not each pair.")
        evaluate = workflow["jobs"]["evaluate"]
        self.assertEqual(evaluate["needs"], "pairs")
        self.assertEqual(evaluate["if"], "always()")


if __name__ == "__main__":
    unittest.main()
