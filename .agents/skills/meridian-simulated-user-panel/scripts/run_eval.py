#!/usr/bin/env python3
"""Deterministic eval helper for the meridian-simulated-user-panel skill."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


SKILL_DIR = Path(__file__).resolve().parents[1]
DEFAULT_EVALS_PATH = SKILL_DIR / "evals" / "evals.json"
DEFAULT_BASELINE_PATH = SKILL_DIR / "evals" / "benchmark_baseline.json"
DEFAULT_OUTPUT_DIR = SKILL_DIR / "tmp-evals"

SECTION_KEYS = {
    "executive_summary": "Executive Summary",
    "panel": "Panel",
    "persona_findings": "Persona Findings",
    "cross_persona_tensions": "Cross-Persona Tensions",
    "owner_actions": "Owner Actions",
    "release_recommendation": "Release Recommendation",
    "confidence_notes": "Confidence Notes",
}

RUBRIC_ALIASES = {
    "workflow fit": ["workflow fit"],
    "trust / controls": ["trust / controls", "trust controls"],
    "time-to-value": ["time-to-value", "time to value"],
    "data confidence": ["data confidence"],
    "extensibility": ["extensibility"],
    "learning curve": ["learning curve"],
}

DISAGREEMENT_TERMS = (
    "disagree",
    "disagreement",
    "tension",
    "trade-off",
    "tradeoff",
    "conflict",
    "however",
    "while",
)

STOPWORDS = {
    "a",
    "an",
    "and",
    "are",
    "as",
    "at",
    "be",
    "but",
    "by",
    "for",
    "from",
    "has",
    "have",
    "in",
    "into",
    "is",
    "it",
    "of",
    "on",
    "or",
    "that",
    "the",
    "their",
    "there",
    "this",
    "to",
    "with",
}


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def slugify(value: str) -> str:
    cleaned = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return cleaned or "eval"


def normalize_heading(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", value.lower()).strip("_")


def normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", value.strip())


def find_eval(eval_payload: dict[str, Any], eval_id: int) -> dict[str, Any]:
    for item in eval_payload.get("evals", []):
        if int(item["id"]) == int(eval_id):
            return item
    raise SystemExit(f"Eval id {eval_id} not found in {DEFAULT_EVALS_PATH}")


def build_prompt_markdown(skill_name: str, eval_item: dict[str, Any]) -> str:
    manifest = json.dumps(eval_item["artifact_manifest"], indent=2)
    return (
        f"# Eval {eval_item['id']} — {eval_item['title']}\n\n"
        f"## Skill\n\n`{skill_name}`\n\n"
        f"## Instruction\n\n{eval_item['instruction']}\n\n"
        f"## Review Manifest\n\n```json\n{manifest}\n```\n\n"
        f"## Expected Output\n\n{eval_item['expected_output']}\n"
    )


def materialize_eval(eval_payload: dict[str, Any], eval_item: dict[str, Any], output_root: Path) -> Path:
    skill_name = eval_payload.get("skill_name", SKILL_DIR.name)
    slug = slugify(eval_item["slug"])
    eval_dir = output_root / f"eval-{int(eval_item['id']):02d}-{slug}"
    eval_dir.mkdir(parents=True, exist_ok=True)
    (eval_dir / "prompt.md").write_text(
        build_prompt_markdown(skill_name, eval_item),
        encoding="utf-8",
    )
    write_json(eval_dir / "manifest.json", eval_item["artifact_manifest"])
    write_json(eval_dir / "expectations.json", eval_item["checks"])
    write_json(eval_dir / "eval.json", eval_item)
    (eval_dir / "README.txt").write_text(
        "Save a model response as `response.md`, then run:\n"
        "python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py "
        f"score --workspace {eval_dir}\n",
        encoding="utf-8",
    )
    return eval_dir


def parse_sections(text: str) -> dict[str, str]:
    matches = list(re.finditer(r"^##\s+(.+)$", text, flags=re.MULTILINE))
    sections: dict[str, str] = {}
    if not matches:
        return sections
    for index, match in enumerate(matches):
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        sections[normalize_heading(match.group(1))] = text[start:end].strip()
    return sections


def parse_persona_blocks(persona_section: str) -> list[dict[str, str]]:
    matches = list(re.finditer(r"^###\s+(.+)$", persona_section, flags=re.MULTILINE))
    personas: list[dict[str, str]] = []
    for index, match in enumerate(matches):
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(persona_section)
        personas.append(
            {
                "persona": normalize_text(match.group(1)),
                "content": persona_section[start:end].strip(),
            }
        )
    return personas


def extract_first_match(text: str, terms: list[str]) -> str | None:
    lowered = text.lower()
    for term in terms:
        if term.lower() in lowered:
            return term
    return None


def line_with_alias(text: str, aliases: list[str]) -> str | None:
    for line in text.splitlines():
        lowered = line.lower()
        if any(alias in lowered for alias in aliases) and re.search(r"\b[1-5]/5\b", line):
            return normalize_text(line)
    return None


def extract_owner_priorities(owner_actions_section: str) -> list[dict[str, str]]:
    priorities: list[dict[str, str]] = []
    for line in owner_actions_section.splitlines():
        match = re.match(r"^\s*[-*]?\s*(Now|Next|Later):\s*(.+)$", line.strip(), flags=re.IGNORECASE)
        if match:
            priorities.append(
                {
                    "bucket": match.group(1).title(),
                    "text": normalize_text(match.group(2)),
                }
            )
    return priorities


def normalize_cluster(value: str) -> str:
    tokens = [
        token
        for token in re.findall(r"[a-z0-9]+", value.lower())
        if token not in STOPWORDS and len(token) > 2
    ]
    return " ".join(tokens[:6]) or "general complaint"


def extract_repeated_complaints(
    sections: dict[str, str],
    personas: list[dict[str, str]],
) -> list[dict[str, Any]]:
    clusters: list[dict[str, Any]] = []
    tensions = sections.get("cross_persona_tensions", "")
    for line in tensions.splitlines():
        cleaned = normalize_text(line.lstrip("-* "))
        lowered = cleaned.lower()
        if cleaned and any(term in lowered for term in ("shared", "repeat", "common", "cluster")):
            clusters.append({"cluster": cleaned, "count": 1})
    if clusters:
        return clusters[:5]

    samples: dict[str, str] = {}
    counts: Counter[str] = Counter()
    for persona in personas:
        for label in ("Didn't like", "Missing or risky"):
            match = re.search(rf"-\s*{re.escape(label)}:\s*(.+)", persona["content"], flags=re.IGNORECASE)
            if match:
                normalized = normalize_cluster(match.group(1))
                counts[normalized] += 1
                samples.setdefault(normalized, normalize_text(match.group(1)))
    results = [
        {"cluster": samples[key], "count": count}
        for key, count in counts.most_common()
        if count > 1
    ]
    return results[:5]


def extract_disagreement_signals(section_text: str) -> list[str]:
    signals: list[str] = []
    for line in section_text.splitlines():
        cleaned = normalize_text(line.lstrip("-* "))
        lowered = cleaned.lower()
        if cleaned and any(term in lowered for term in DISAGREEMENT_TERMS):
            signals.append(cleaned)
    return signals[:5]


def render_table(headers: list[str], rows: list[list[str]]) -> str:
    widths = [len(header) for header in headers]
    for row in rows:
        for index, cell in enumerate(row):
            widths[index] = max(widths[index], len(cell))
    def render_row(row: list[str]) -> str:
        return " | ".join(cell.ljust(widths[index]) for index, cell in enumerate(row))
    divider = "-+-".join("-" * width for width in widths)
    output = [render_row(headers), divider]
    output.extend(render_row(row) for row in rows)
    return "\n".join(output)


def score_eval(
    eval_payload: dict[str, Any],
    eval_item: dict[str, Any],
    response_text: str,
    baseline_payload: dict[str, Any],
) -> dict[str, Any]:
    sections = parse_sections(response_text)
    personas = parse_persona_blocks(sections.get("persona_findings", ""))
    section_results = {key: key in sections for key in eval_payload.get("required_sections", [])}
    persona_scores = []
    for persona in personas:
        dimensions: dict[str, bool] = {}
        for dimension, aliases in RUBRIC_ALIASES.items():
            dimensions[dimension] = line_with_alias(persona["content"], aliases) is not None
        persona_scores.append(
            {
                "persona": persona["persona"],
                "rubric_dimensions": dimensions,
            }
        )

    checks: list[dict[str, Any]] = []
    for section_key, present in section_results.items():
        checks.append(
            {
                "name": f"Section present: {SECTION_KEYS.get(section_key, section_key)}",
                "passed": present,
                "evidence": SECTION_KEYS.get(section_key, section_key) if present else "missing",
            }
        )

    check_spec = eval_item.get("checks", {})
    minimum_personas = int(check_spec.get("minimum_personas", 0))
    checks.append(
        {
            "name": f"At least {minimum_personas} personas",
            "passed": len(personas) >= minimum_personas,
            "evidence": f"persona_count={len(personas)}",
        }
    )

    persona_names = {persona["persona"].lower() for persona in personas}
    for required_persona in check_spec.get("required_personas_all", []):
        checks.append(
            {
                "name": f"Persona included: {required_persona}",
                "passed": required_persona.lower() in persona_names,
                "evidence": required_persona if required_persona.lower() in persona_names else "missing",
            }
        )

    required_any = check_spec.get("required_personas_any", [])
    if required_any:
        matched_persona = next((name for name in required_any if name.lower() in persona_names), None)
        checks.append(
            {
                "name": f"One optional persona included: {' / '.join(required_any)}",
                "passed": matched_persona is not None,
                "evidence": matched_persona or "missing",
            }
        )

    all_text = response_text.lower()
    for phrase_check in check_spec.get("required_phrases", []):
        matched = extract_first_match(all_text, phrase_check.get("any_of", []))
        checks.append(
            {
                "name": phrase_check["label"],
                "passed": matched is not None,
                "evidence": matched or "missing",
            }
        )

    for forbidden in check_spec.get("forbidden_phrases", []):
        checks.append(
            {
                "name": f"Forbidden phrase absent: {forbidden}",
                "passed": forbidden.lower() not in all_text,
                "evidence": "absent" if forbidden.lower() not in all_text else forbidden,
            }
        )

    owner_actions = sections.get("owner_actions", "")
    for bucket in check_spec.get("required_priority_buckets", []):
        present = re.search(rf"\b{re.escape(bucket.lower())}\s*:", owner_actions.lower()) is not None
        checks.append(
            {
                "name": f"Priority bucket present: {bucket}",
                "passed": present,
                "evidence": bucket if present else "missing",
            }
        )

    confidence_notes = sections.get("confidence_notes", "")
    if check_spec.get("require_verified"):
        verified_present = "verified" in confidence_notes.lower()
        checks.append(
            {
                "name": "Confidence Notes include Verified",
                "passed": verified_present,
                "evidence": "Verified" if verified_present else "missing",
            }
        )
    if check_spec.get("require_inferred"):
        inferred_present = "inferred" in confidence_notes.lower()
        checks.append(
            {
                "name": "Confidence Notes include Inferred",
                "passed": inferred_present,
                "evidence": "Inferred" if inferred_present else "missing",
            }
        )
    if check_spec.get("require_missing_evidence"):
        missing_evidence_present = "missing evidence" in confidence_notes.lower()
        checks.append(
            {
                "name": "Confidence Notes include Missing evidence",
                "passed": missing_evidence_present,
                "evidence": "Missing evidence" if missing_evidence_present else "missing",
            }
        )

    disagreements = extract_disagreement_signals(sections.get("cross_persona_tensions", ""))
    if check_spec.get("require_disagreement_signal"):
        checks.append(
            {
                "name": "Cross-persona disagreement signal present",
                "passed": bool(disagreements),
                "evidence": disagreements[0] if disagreements else "missing",
            }
        )

    release_terms = check_spec.get("required_release_terms_any", [])
    if release_terms:
        release_section = sections.get("release_recommendation", "").lower()
        matched_release = extract_first_match(release_section, release_terms)
        checks.append(
            {
                "name": f"Mode-appropriate recommendation: {' / '.join(release_terms)}",
                "passed": matched_release is not None,
                "evidence": matched_release or "missing",
            }
        )

    rubric_check_passed = bool(persona_scores) and all(
        all(score["rubric_dimensions"].values()) for score in persona_scores
    )
    checks.append(
        {
            "name": "Every persona includes all rubric dimensions",
            "passed": rubric_check_passed,
            "evidence": f"persona_count={len(persona_scores)}",
        }
    )

    passed = sum(1 for check in checks if check["passed"])
    total = len(checks)
    failed = total - passed
    pass_rate = round(passed / total, 4) if total else 0.0

    baseline_map = {int(item["eval_id"]): item for item in baseline_payload.get("baselines", [])}
    baseline = baseline_map.get(int(eval_item["id"]), {})
    accepted = float(baseline.get("accepted_pass_rate", 0.0))
    delta_pp = round((pass_rate - accepted) * 100.0, 2)

    result = {
        "schema_version": eval_payload.get("schema_version", "2026-04-14"),
        "generated_at": datetime.now(UTC).isoformat(),
        "eval_id": int(eval_item["id"]),
        "title": eval_item["title"],
        "mode": eval_item["mode"],
        "artifact_type": eval_item["artifact_type"],
        "required_sections": section_results,
        "persona_scores": persona_scores,
        "checks": checks,
        "summary": {
            "passed": passed,
            "failed": failed,
            "total": total,
            "pass_rate": pass_rate,
        },
        "owner_priorities": extract_owner_priorities(owner_actions),
        "repeated_complaints": extract_repeated_complaints(sections, personas),
        "disagreement_signals": disagreements,
        "benchmark": {
            "accepted_pass_rate": accepted,
            "actual_pass_rate": pass_rate,
            "delta_pp": delta_pp,
            "meets_baseline": pass_rate >= accepted,
        },
    }
    return result


def print_score_table(result: dict[str, Any]) -> None:
    rows = [
        ["Eval", f"{result['eval_id']}"],
        ["Mode", result["mode"]],
        ["Artifact", result["artifact_type"]],
        ["Pass rate", f"{result['summary']['pass_rate']:.0%}"],
        ["Baseline delta", f"{result['benchmark']['delta_pp']:+.2f}pp"],
        ["Meets baseline", "yes" if result["benchmark"]["meets_baseline"] else "no"],
    ]
    print(render_table(["Field", "Value"], rows))


def collect_grading_files(root: Path) -> list[Path]:
    if root.is_file():
        return [root]
    paths = {path for path in root.rglob("grading.json")}
    paths.update(root.rglob("*.grading.json"))
    return sorted(paths)


def aggregate_scores(
    eval_payload: dict[str, Any],
    baseline_payload: dict[str, Any],
    score_files: list[Path],
) -> dict[str, Any]:
    results = [load_json(path) for path in score_files]
    overall_pass_rate = round(
        sum(item["summary"]["pass_rate"] for item in results) / len(results),
        4,
    ) if results else 0.0

    complaint_counts: Counter[str] = Counter()
    complaint_samples: dict[str, str] = {}
    priority_counts: Counter[tuple[str, str]] = Counter()

    for result in results:
        for complaint in result.get("repeated_complaints", []):
            key = normalize_cluster(complaint["cluster"])
            complaint_counts[key] += int(complaint.get("count", 1))
            complaint_samples.setdefault(key, complaint["cluster"])
        for priority in result.get("owner_priorities", []):
            key = (priority["bucket"], normalize_text(priority["text"]))
            priority_counts[key] += 1

    eval_map = {int(item["id"]): item for item in eval_payload.get("evals", [])}
    baselines = {int(item["eval_id"]): item for item in baseline_payload.get("baselines", [])}
    eval_rows = []
    for result in results:
        eval_id = int(result["eval_id"])
        eval_rows.append(
            {
                "eval_id": eval_id,
                "title": eval_map.get(eval_id, {}).get("title", result.get("title", f"Eval {eval_id}")),
                "pass_rate": result["summary"]["pass_rate"],
                "accepted_pass_rate": baselines.get(eval_id, {}).get("accepted_pass_rate", 0.0),
                "delta_pp": result["benchmark"]["delta_pp"],
                "meets_baseline": result["benchmark"]["meets_baseline"],
            }
        )

    scored_ids = {int(item["eval_id"]) for item in eval_rows}
    missing_evals = [
        {"eval_id": int(item["id"]), "title": item["title"]}
        for item in eval_payload.get("evals", [])
        if int(item["id"]) not in scored_ids
    ]
    is_complete = len(missing_evals) == 0
    overall_minimum_pass_rate = baseline_payload.get("overall_minimum_pass_rate", 0.0)

    return {
        "schema_version": eval_payload.get("schema_version", "2026-04-14"),
        "generated_at": datetime.now(UTC).isoformat(),
        "evaluated_count": len(results),
        "overall": {
            "mean_pass_rate": overall_pass_rate,
            "overall_minimum_pass_rate": overall_minimum_pass_rate,
            "is_complete": is_complete,
            "missing_eval_count": len(missing_evals),
            "meets_baseline": is_complete and overall_pass_rate >= overall_minimum_pass_rate,
        },
        "evals": eval_rows,
        "repeated_complaints": [
            {"cluster": complaint_samples[key], "count": count}
            for key, count in complaint_counts.most_common(10)
        ],
        "owner_priorities": [
            {"bucket": key[0], "text": key[1], "count": count}
            for key, count in priority_counts.most_common(10)
        ],
        "missing_evals": missing_evals,
    }


def print_aggregate_table(aggregate: dict[str, Any]) -> None:
    rows = [
        [
            str(item["eval_id"]),
            item["title"][:42],
            f"{item['pass_rate']:.0%}",
            f"{item['delta_pp']:+.2f}pp",
            "yes" if item["meets_baseline"] else "no",
        ]
        for item in aggregate.get("evals", [])
    ]
    if rows:
        print(render_table(["Eval", "Title", "Pass", "Delta", "Baseline"], rows))
    print()
    print(
        render_table(
            ["Field", "Value"],
            [
                ["Evaluated", str(aggregate["evaluated_count"])],
                ["Mean pass rate", f"{aggregate['overall']['mean_pass_rate']:.0%}"],
                ["Overall baseline", f"{aggregate['overall']['overall_minimum_pass_rate']:.0%}"],
                ["Eval set complete", "yes" if aggregate["overall"].get("is_complete") else "no"],
                ["Meets overall baseline", "yes" if aggregate["overall"]["meets_baseline"] else "no"],
            ],
        )
    )


def command_list(eval_payload: dict[str, Any]) -> int:
    rows = [
        [str(item["id"]), item["mode"], item["artifact_type"], item["title"]]
        for item in eval_payload.get("evals", [])
    ]
    print(render_table(["Id", "Mode", "Artifact", "Title"], rows))
    return 0


def command_materialize(args: argparse.Namespace) -> int:
    eval_payload = load_json(Path(args.eval_set))
    evals = eval_payload.get("evals", [])
    if args.eval_id is not None:
        evals = [find_eval(eval_payload, args.eval_id)]
    output_root = Path(args.output_dir)
    output_root.mkdir(parents=True, exist_ok=True)
    created = [str(materialize_eval(eval_payload, item, output_root)) for item in evals]
    print(json.dumps({"eval_count": len(created), "workspaces": created}, indent=2))
    return 0


def command_score(args: argparse.Namespace) -> int:
    eval_payload = load_json(Path(args.eval_set))
    baseline_payload = load_json(Path(args.baseline))

    workspace = Path(args.workspace) if args.workspace else None
    response_path = Path(args.response) if args.response else None

    if workspace is not None:
        eval_item = load_json(workspace / "eval.json")
        response_path = workspace / "response.md"
        if not response_path.exists():
            raise SystemExit(f"Response file not found: {response_path}")
        output_path = Path(args.output) if args.output else workspace / "grading.json"
    else:
        if response_path is None or args.eval_id is None:
            raise SystemExit("Use --workspace or provide both --response and --eval-id.")
        eval_item = find_eval(eval_payload, args.eval_id)
        output_path = Path(args.output) if args.output else response_path.with_suffix(".grading.json")

    result = score_eval(
        eval_payload=eval_payload,
        eval_item=eval_item,
        response_text=response_path.read_text(encoding="utf-8"),
        baseline_payload=baseline_payload,
    )
    write_json(output_path, result)
    print_score_table(result)
    print()
    print(f"Saved grading JSON to {output_path}")
    return 0


def command_aggregate(args: argparse.Namespace) -> int:
    eval_payload = load_json(Path(args.eval_set))
    baseline_payload = load_json(Path(args.baseline))
    input_path = Path(args.input)
    score_files = collect_grading_files(input_path)
    if not score_files:
        raise SystemExit(f"No grading.json files found under {input_path}")
    aggregate = aggregate_scores(eval_payload, baseline_payload, score_files)
    output_path = Path(args.output) if args.output else input_path / "aggregate.json"
    write_json(output_path, aggregate)
    print_aggregate_table(aggregate)
    print()
    print(f"Saved aggregate JSON to {output_path}")
    return 0


def command_compare_baseline(args: argparse.Namespace) -> int:
    aggregate = load_json(Path(args.aggregate))
    rows = [
        [
            str(item["eval_id"]),
            item["title"][:42],
            f"{item['pass_rate']:.0%}",
            f"{item['accepted_pass_rate']:.0%}",
            f"{item['delta_pp']:+.2f}pp",
            "yes" if item["meets_baseline"] else "no",
        ]
        for item in aggregate.get("evals", [])
    ]
    if rows:
        print(render_table(["Eval", "Title", "Actual", "Baseline", "Delta", "Meets"], rows))
    print()
    print(
        render_table(
            ["Field", "Value"],
            [
                ["Mean pass rate", f"{aggregate['overall']['mean_pass_rate']:.0%}"],
                ["Overall baseline", f"{aggregate['overall']['overall_minimum_pass_rate']:.0%}"],
                ["Eval set complete", "yes" if aggregate["overall"].get("is_complete") else "no"],
                ["Meets overall baseline", "yes" if aggregate["overall"]["meets_baseline"] else "no"],
                ["Missing evals", str(len(aggregate.get('missing_evals', [])))],
            ],
        )
    )
    if aggregate.get("missing_evals"):
        print()
        missing_rows = [
            [str(item["eval_id"]), item["title"][:48]] for item in aggregate["missing_evals"]
        ]
        print(render_table(["Missing Eval", "Title"], missing_rows))
    return 0


def legacy_main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Legacy interface for materializing simulated-user-panel evals."
    )
    parser.add_argument("--eval-set", default=str(DEFAULT_EVALS_PATH))
    parser.add_argument("--eval-id", type=int)
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR))
    parser.add_argument("--list", action="store_true")
    args = parser.parse_args(argv)
    eval_payload = load_json(Path(args.eval_set))
    if args.list:
        return command_list(eval_payload)
    return command_materialize(args)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Deterministic eval helper for meridian-simulated-user-panel"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    list_parser = subparsers.add_parser("list", help="List available evals")
    list_parser.add_argument("--eval-set", default=str(DEFAULT_EVALS_PATH))

    materialize_parser = subparsers.add_parser("materialize", help="Create manual eval workspaces")
    materialize_parser.add_argument("--eval-set", default=str(DEFAULT_EVALS_PATH))
    materialize_parser.add_argument("--eval-id", type=int)
    materialize_parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR))

    score_parser = subparsers.add_parser("score", help="Score a response against one eval")
    score_parser.add_argument("--eval-set", default=str(DEFAULT_EVALS_PATH))
    score_parser.add_argument("--baseline", default=str(DEFAULT_BASELINE_PATH))
    score_parser.add_argument("--workspace")
    score_parser.add_argument("--response")
    score_parser.add_argument("--eval-id", type=int)
    score_parser.add_argument("--output")

    aggregate_parser = subparsers.add_parser("aggregate", help="Aggregate grading JSON files")
    aggregate_parser.add_argument("--eval-set", default=str(DEFAULT_EVALS_PATH))
    aggregate_parser.add_argument("--baseline", default=str(DEFAULT_BASELINE_PATH))
    aggregate_parser.add_argument("--input", default=str(DEFAULT_OUTPUT_DIR))
    aggregate_parser.add_argument("--output")

    compare_parser = subparsers.add_parser("compare-baseline", help="Compare aggregate results to baselines")
    compare_parser.add_argument("--aggregate", required=True)

    return parser


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv:
        argv = ["list"]
    if argv[0].startswith("-"):
        return legacy_main(argv)

    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "list":
        return command_list(load_json(Path(args.eval_set)))
    if args.command == "materialize":
        return command_materialize(args)
    if args.command == "score":
        return command_score(args)
    if args.command == "aggregate":
        return command_aggregate(args)
    if args.command == "compare-baseline":
        return command_compare_baseline(args)
    raise SystemExit(f"Unknown command: {args.command}")


if __name__ == "__main__":
    raise SystemExit(main())
