import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "promptfoo_adapter.py"
SPEC = importlib.util.spec_from_file_location("promptfoo_adapter", MODULE_PATH)
promptfoo_adapter = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = promptfoo_adapter
SPEC.loader.exec_module(promptfoo_adapter)


class PromptfooAdapterTests(unittest.TestCase):
    def make_workspace(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        evals = root / "evals.json"
        evals.write_text(
            json.dumps(
                {
                    "skill_name": "sample-skill",
                    "evals": [
                        {
                            "id": 1,
                            "scenario": "A",
                            "description": "Existing docs update",
                            "prompt": "Use the skill to certify the docs update.",
                            "assertions": ["Must cite validation", "Must cite docs"],
                        },
                        {
                            "id": 2,
                            "description": "Missing docs",
                            "prompt": "Use the skill to create missing docs.",
                            "assertions": ["Must add a cross-link"],
                        },
                    ],
                }
            ),
            encoding="utf-8",
        )
        return temp, root, evals

    def test_builds_read_only_promptfoo_artifacts_from_selected_eval(self):
        temp, root, evals = self.make_workspace()
        self.addCleanup(temp.cleanup)
        output = root / "artifacts"
        traces = root / "trace_outputs.json"
        traces.write_text(
            json.dumps({"sample-skill:1": {"output": "Validation passed. Docs updated."}}),
            encoding="utf-8",
        )

        cases = promptfoo_adapter.select_cases(promptfoo_adapter.load_eval_cases(evals), ["1"])
        result = promptfoo_adapter.write_promptfoo_artifacts(
            evals_path=evals,
            output_dir=output,
            cases=cases,
            trace_outputs=promptfoo_adapter.load_trace_output_map(traces),
            judge_provider="openai:gpt-5-mini",
            include_llm_rubric=True,
        )

        self.assertEqual(1, result["case_count"])
        self.assertEqual(0, result["missing_trace_output_count"])
        config = json.loads((output / "promptfooconfig.yaml").read_text(encoding="utf-8"))
        self.assertEqual(["{{prompt}}"], config["prompts"])
        self.assertEqual("file://trace_output_provider.py", config["providers"][0]["id"])
        self.assertEqual("sample-skill:1", config["tests"][0]["vars"]["case_id"])
        self.assertEqual("llm-rubric", config["tests"][0]["assert"][1]["type"])
        self.assertEqual("openai:gpt-5-mini", config["tests"][0]["assert"][1]["provider"])

    def test_missing_trace_outputs_are_reported_without_blocking_generation(self):
        temp, root, evals = self.make_workspace()
        self.addCleanup(temp.cleanup)
        output = root / "artifacts"
        cases = promptfoo_adapter.load_eval_cases(evals)

        result = promptfoo_adapter.write_promptfoo_artifacts(
            evals_path=evals,
            output_dir=output,
            cases=cases,
            trace_outputs={},
            judge_provider=None,
            include_llm_rubric=False,
        )

        self.assertEqual(2, result["missing_trace_output_count"])
        trace_payload = json.loads((output / "trace_outputs.json").read_text(encoding="utf-8"))
        self.assertTrue(trace_payload["cases"]["sample-skill:1"]["missing_output"])
        config = json.loads((output / "promptfooconfig.yaml").read_text(encoding="utf-8"))
        self.assertEqual(1, len(config["tests"][0]["assert"]))

    def test_invalid_eval_selection_reports_missing_ids(self):
        temp, _, evals = self.make_workspace()
        self.addCleanup(temp.cleanup)

        with self.assertRaisesRegex(promptfoo_adapter.PromptfooAdapterError, "not found"):
            promptfoo_adapter.select_cases(promptfoo_adapter.load_eval_cases(evals), ["99"])


if __name__ == "__main__":
    unittest.main()
