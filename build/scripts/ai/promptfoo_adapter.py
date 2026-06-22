#!/usr/bin/env python3
"""Generate read-only Promptfoo configs from Meridian skill eval manifests."""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_EVALS_PATH = REPO_ROOT / ".codex" / "skills" / "meridian-implementation-assurance" / "evals" / "evals.json"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "artifacts" / "ai" / "promptfoo"

TRACE_OUTPUT_PROVIDER = r'''from __future__ import annotations

import json
from pathlib import Path


def call_api(prompt: str, options: dict, context: dict) -> dict:
    config = options.get("config", {})
    trace_outputs = json.loads(Path(config["trace_outputs_path"]).read_text(encoding="utf-8"))
    case_id = (context.get("vars") or {}).get("case_id")
    entry = (trace_outputs.get("cases") or {}).get(case_id) or {}
    return {
        "output": entry.get("output", ""),
        "metadata": {
            "case_id": case_id,
            "prompt": prompt,
            "source": entry.get("source", "trace-output"),
            "missing_output": bool(entry.get("missing_output", False)),
            **(entry.get("metadata") or {}),
        },
    }
'''


class PromptfooAdapterError(Exception):
    """Raised when the adapter cannot safely build or run a Promptfoo suite."""


@dataclass(frozen=True)
class EvalCase:
    case_id: str
    eval_id: str
    skill_name: str
    description: str
    prompt: str
    assertions: tuple[str, ...]
    scenario: str | None


def configure_stdio() -> None:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            try:
                stream.reconfigure(encoding="utf-8", errors="replace")
            except OSError:
                pass


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise PromptfooAdapterError(f"JSON file not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise PromptfooAdapterError(f"Invalid JSON in {path}: {exc}") from exc


def load_eval_cases(evals_path: Path) -> list[EvalCase]:
    payload = load_json(evals_path)
    if not isinstance(payload, dict):
        raise PromptfooAdapterError("Eval manifest must be a JSON object.")

    skill_name = str(payload.get("skill_name") or evals_path.parents[1].name)
    raw_cases = payload.get("evals")
    if not isinstance(raw_cases, list):
        raise PromptfooAdapterError("Eval manifest must contain an 'evals' list.")

    cases: list[EvalCase] = []
    for index, item in enumerate(raw_cases, start=1):
        if not isinstance(item, dict):
            raise PromptfooAdapterError(f"Eval item {index} must be an object.")
        prompt = str(item.get("prompt") or "").strip()
        if not prompt:
            raise PromptfooAdapterError(f"Eval item {index} must define a non-empty prompt.")
        eval_id = str(item.get("id") or index)
        raw_assertions = item.get("assertions") or []
        if not isinstance(raw_assertions, list):
            raise PromptfooAdapterError(f"Eval item {eval_id} assertions must be a list.")
        assertions = tuple(str(value).strip() for value in raw_assertions if str(value).strip())
        cases.append(
            EvalCase(
                case_id=f"{skill_name}:{eval_id}",
                eval_id=eval_id,
                skill_name=skill_name,
                description=str(item.get("description") or f"Eval {eval_id}").strip(),
                prompt=prompt,
                assertions=assertions,
                scenario=str(item["scenario"]).strip() if item.get("scenario") else None,
            )
        )
    return cases


def select_cases(cases: list[EvalCase], eval_ids: list[str]) -> list[EvalCase]:
    if not eval_ids:
        return cases
    requested = {str(value) for value in eval_ids}
    selected = [case for case in cases if case.eval_id in requested or case.case_id in requested]
    found = {case.eval_id for case in selected} | {case.case_id for case in selected}
    missing = sorted(requested - found)
    if missing:
        raise PromptfooAdapterError(f"Requested eval id(s) not found: {', '.join(missing)}")
    return selected


def load_trace_output_map(path: Path | None) -> dict[str, dict[str, Any]]:
    if path is None:
        return {}
    payload = load_json(path)
    if isinstance(payload, dict) and isinstance(payload.get("cases"), dict):
        return {
            str(case_id): normalize_trace_output(entry)
            for case_id, entry in payload["cases"].items()
        }
    if isinstance(payload, dict):
        return {
            str(case_id): normalize_trace_output(entry)
            for case_id, entry in payload.items()
        }
    if isinstance(payload, list):
        mapped: dict[str, dict[str, Any]] = {}
        for index, item in enumerate(payload, start=1):
            if not isinstance(item, dict):
                raise PromptfooAdapterError(f"Trace output item {index} must be an object.")
            case_id = item.get("case_id") or item.get("eval_id") or item.get("id")
            if not case_id:
                raise PromptfooAdapterError(f"Trace output item {index} is missing case_id/eval_id/id.")
            mapped[str(case_id)] = normalize_trace_output(item)
        return mapped
    raise PromptfooAdapterError("--trace-outputs must be a JSON object or list.")


def normalize_trace_output(value: Any) -> dict[str, Any]:
    if isinstance(value, str):
        return {"output": value, "source": "trace-output"}
    if not isinstance(value, dict):
        return {"output": str(value), "source": "trace-output"}
    output = value.get("output", value.get("answer", value.get("response", "")))
    metadata = value.get("metadata") if isinstance(value.get("metadata"), dict) else {}
    return {
        "output": str(output or ""),
        "source": str(value.get("source") or "trace-output"),
        "metadata": metadata,
    }


def trace_entry_for_case(case: EvalCase, trace_outputs: dict[str, dict[str, Any]]) -> dict[str, Any]:
    for key in (case.case_id, case.eval_id):
        if key in trace_outputs:
            entry = dict(trace_outputs[key])
            entry["missing_output"] = False
            return entry
    return {
        "output": "",
        "source": "missing-trace-output",
        "missing_output": True,
        "metadata": {"description": case.description},
    }


def build_rubric(case: EvalCase) -> str:
    if not case.assertions:
        return f"Evaluate whether the output satisfies this eval case: {case.description}"
    bullets = "\n".join(f"- {assertion}" for assertion in case.assertions)
    return (
        "Evaluate the output against the Meridian skill eval requirements below. "
        "Pass only when the output materially satisfies the requirements.\n"
        f"{bullets}"
    )


def promptfoo_test_from_case(case: EvalCase, judge_provider: str | None, include_llm_rubric: bool) -> dict[str, Any]:
    assertions: list[dict[str, Any]] = [
        {
            "type": "javascript",
            "value": "typeof output === 'string' && output.trim().length > 0",
        }
    ]
    if include_llm_rubric:
        rubric: dict[str, Any] = {
            "type": "llm-rubric",
            "threshold": 0.8,
            "value": build_rubric(case),
        }
        if judge_provider:
            rubric["provider"] = judge_provider
        assertions.append(rubric)

    return {
        "description": case.description,
        "vars": {
            "case_id": case.case_id,
            "eval_id": case.eval_id,
            "skill_name": case.skill_name,
            "prompt": case.prompt,
        },
        "metadata": {
            "case_id": case.case_id,
            "eval_id": case.eval_id,
            "skill_name": case.skill_name,
            "scenario": case.scenario,
            "mode": "trace-output-read-only",
        },
        "assert": assertions,
    }


def write_promptfoo_artifacts(
    *,
    evals_path: Path,
    output_dir: Path,
    cases: list[EvalCase],
    trace_outputs: dict[str, dict[str, Any]],
    judge_provider: str | None,
    include_llm_rubric: bool,
) -> dict[str, Any]:
    output_dir.mkdir(parents=True, exist_ok=True)
    provider_path = output_dir / "trace_output_provider.py"
    trace_outputs_path = output_dir / "trace_outputs.json"
    config_path = output_dir / "promptfooconfig.yaml"
    adapter_result_path = output_dir / "adapter_result.json"
    promptfoo_results_path = output_dir / "promptfoo_results.json"

    provider_path.write_text(TRACE_OUTPUT_PROVIDER, encoding="utf-8")
    trace_payload = {
        "schema_version": "2026-06-09",
        "mode": "trace-output-read-only",
        "generated_at": now_iso(),
        "source_evals": str(evals_path),
        "cases": {
            case.case_id: trace_entry_for_case(case, trace_outputs)
            for case in cases
        },
    }
    trace_outputs_path.write_text(json.dumps(trace_payload, indent=2) + "\n", encoding="utf-8")

    tests = [
        promptfoo_test_from_case(case, judge_provider=judge_provider, include_llm_rubric=include_llm_rubric)
        for case in cases
    ]
    config = {
        "description": "Meridian read-only Promptfoo gate generated from canonical skill evals",
        "prompts": ["{{prompt}}"],
        "providers": [
            {
                "id": "file://trace_output_provider.py",
                "label": "trace-output-read-only",
                "config": {"trace_outputs_path": str(trace_outputs_path.resolve())},
            }
        ],
        "tests": tests,
    }
    # JSON is valid YAML; this avoids adding a YAML dependency for a generator.
    config_path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")

    missing_outputs = [
        case.case_id for case in cases
        if trace_payload["cases"][case.case_id].get("missing_output")
    ]
    adapter_result = {
        "schema_version": "2026-06-09",
        "generated_at": now_iso(),
        "mode": "trace-output-read-only",
        "source_evals": str(evals_path),
        "case_count": len(cases),
        "missing_trace_output_count": len(missing_outputs),
        "missing_trace_outputs": missing_outputs,
        "promptfoo_ready": len(missing_outputs) == 0,
        "paths": {
            "output_dir": str(output_dir),
            "config": str(config_path),
            "provider": str(provider_path),
            "trace_outputs": str(trace_outputs_path),
            "promptfoo_results": str(promptfoo_results_path),
        },
        "run": None,
    }
    adapter_result_path.write_text(json.dumps(adapter_result, indent=2) + "\n", encoding="utf-8")
    return adapter_result


def run_promptfoo(output_dir: Path, result_path: Path, command: str) -> dict[str, Any]:
    parts = command.split()
    if not parts:
        raise PromptfooAdapterError("--promptfoo-command cannot be empty.")
    executable = shutil.which(parts[0])
    if executable is None:
        raise PromptfooAdapterError(f"Promptfoo command executable not found: {parts[0]}")
    cmd = [
        *parts,
        "eval",
        "--config",
        "promptfooconfig.yaml",
        "--output",
        result_path.name,
    ]
    proc = subprocess.run(cmd, cwd=output_dir, capture_output=True, text=True, timeout=600)
    return {
        "command": cmd,
        "return_code": proc.returncode,
        "stdout_tail": proc.stdout[-2000:],
        "stderr_tail": proc.stderr[-2000:],
        "result_path": str(result_path),
    }


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate a read-only Promptfoo suite from Meridian skill evals.",
    )
    parser.add_argument("--evals", type=Path, default=DEFAULT_EVALS_PATH, help=f"Eval manifest path (default: {DEFAULT_EVALS_PATH}).")
    parser.add_argument("--eval-id", action="append", default=[], help="Eval id or case id to include. Repeatable. Defaults to all cases.")
    parser.add_argument("--trace-outputs", type=Path, help="Optional JSON map/list of case_id or eval_id to existing trace outputs.")
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR, help=f"Artifact output directory (default: {DEFAULT_OUTPUT_DIR}).")
    parser.add_argument("--judge-provider", help="Optional Promptfoo provider for llm-rubric assertions, for example openai:gpt-5-mini.")
    parser.add_argument("--no-llm-rubric", action="store_true", help="Emit only deterministic non-empty-output assertions.")
    parser.add_argument("--run", action="store_true", help="Run Promptfoo after generating artifacts. Requires complete trace outputs.")
    parser.add_argument("--allow-missing-trace-outputs", action="store_true", help="Allow --run even when selected cases lack trace outputs.")
    parser.add_argument("--promptfoo-command", default="npx promptfoo@latest", help="Promptfoo command prefix used with --run.")
    parser.add_argument("--summary", action="store_true", help="Print a compact summary.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    configure_stdio()
    args = parse_args(argv)
    try:
        evals_path = args.evals.resolve()
        output_dir = args.output_dir.resolve()
        cases = select_cases(load_eval_cases(evals_path), [str(value) for value in args.eval_id])
        if not cases:
            raise PromptfooAdapterError("No eval cases selected.")
        trace_outputs = load_trace_output_map(args.trace_outputs.resolve() if args.trace_outputs else None)
        adapter_result = write_promptfoo_artifacts(
            evals_path=evals_path,
            output_dir=output_dir,
            cases=cases,
            trace_outputs=trace_outputs,
            judge_provider=args.judge_provider,
            include_llm_rubric=not args.no_llm_rubric,
        )
        if args.run:
            if adapter_result["missing_trace_output_count"] and not args.allow_missing_trace_outputs:
                raise PromptfooAdapterError(
                    "--run requires trace outputs for every selected eval unless --allow-missing-trace-outputs is set."
                )
            run_result = run_promptfoo(
                output_dir,
                Path(adapter_result["paths"]["promptfoo_results"]),
                args.promptfoo_command,
            )
            adapter_result["run"] = run_result
            Path(output_dir / "adapter_result.json").write_text(json.dumps(adapter_result, indent=2) + "\n", encoding="utf-8")
            if run_result["return_code"] != 0:
                print(json.dumps(adapter_result, indent=2))
                return 1
        if args.summary:
            print(
                "Promptfoo artifacts: "
                f"cases={adapter_result['case_count']} "
                f"missing_trace_outputs={adapter_result['missing_trace_output_count']} "
                f"config={adapter_result['paths']['config']}"
            )
        else:
            print(json.dumps(adapter_result, indent=2))
        return 0
    except PromptfooAdapterError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
