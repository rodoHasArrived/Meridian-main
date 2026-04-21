"""Code-defined Agent Skills provider for Meridian.

Creates a :class:`SkillsProvider` that exposes portable, progressive-disclosure skill packages and code-defined companions:

1. ``meridian-code-review`` — Code review and architecture compliance, available
   both as a file-based skill (discovered from the ``meridian-code-review/``
   directory) and as a code-defined skill.  When both exist the file-based
   version takes precedence; the code-defined definition acts as a fallback
   and hosts **dynamic resources** and **in-process scripts**.

2. ``ai-docs-maintain`` — AI documentation maintenance (freshness checks,
   drift detection, cross-reference validation, stale doc archiving).
   Purely code-defined with scripts that delegate to
   ``build/scripts/docs/ai-docs-maintenance.py``.

3. ``meridian-blueprint`` — Blueprint Mode for translating a single prioritized
   idea into a complete, code-ready technical design document.  Available
   both as a file-based skill (discovered from the ``meridian-blueprint/``
   directory) and as a code-defined skill with a dynamic git-context
   resource and a validate-skill script.

Usage (from a custom agent or MCP server)::

    from .claude.skills.skills_provider import skills_provider

    # Load the skill instructions
    instructions = skills_provider.load_skill("meridian-code-review")

    # Read a static resource
    arch = skills_provider.read_skill_resource("meridian-code-review", "architecture")

    # Read a dynamic resource (re-evaluated on every call)
    stats = skills_provider.read_skill_resource("meridian-code-review", "project-stats")

    # Execute a code-defined script
    result = skills_provider.run_skill_script("meridian-code-review", "validate-skill")

    # AI docs maintenance
    report = skills_provider.run_skill_script("ai-docs-maintain", "run-full")

    # Blueprint Mode
    blueprint_instructions = skills_provider.load_skill("meridian-blueprint")
    patterns = skills_provider.read_skill_resource("meridian-blueprint", "blueprint-patterns")
    pipeline = skills_provider.read_skill_resource("meridian-blueprint", "pipeline-position")
"""

from __future__ import annotations

import argparse as _argparse
import dataclasses as _dataclasses
import datetime as _dt
import json as _json_mod
import logging as _logging
import subprocess
import sys
import time as _time
import warnings as _warnings
from pathlib import Path
from textwrap import dedent
from typing import Any, TypedDict

try:
    from agent_framework import Skill, SkillResource, SkillScript, SkillsProvider

    _HAS_AGENT_FRAMEWORK = True
except ImportError as exc:
    # When imported as a library, fail fast with a clear error instead of
    # silently substituting stubs that will cause confusing attribute errors
    # later when using skills_provider.load_skill(...), read_skill_resource(...),
    # etc.
    if __name__ != "__main__":
        raise ImportError(
            "agent_framework is required to import '.claude.skills.skills_provider'. "
            "Install or enable 'agent_framework' to use the skills_provider API."
        ) from exc

    # Running as a standalone CLI (__main__) — provide minimal no-op stubs so all
    # decorator registrations and constructor calls succeed without
    # agent_framework being installed.
    _HAS_AGENT_FRAMEWORK = False

    # When imported programmatically (not via CLI), warn the caller that the
    # skill provider API (load_skill, read_skill_resource, …) will raise a
    # clear error because agent_framework is absent. The standalone CLI
    # suppresses this warning because it never calls the framework API.
    if __name__ != "__main__":
        _warnings.warn(
            "agent_framework is not installed; skills_provider API methods "
            "(load_skill, read_skill_resource, run_skill_script, …) will raise "
            "a RuntimeError if called. The standalone CLI (list, run-script, "
            "chain, …) is still fully available. Install agent_framework to "
            "use the full provider API.",
            ImportWarning,
            stacklevel=2,
        )

    class _Stub:  # type: ignore[no-redef]
        """Stub used when agent_framework is absent (standalone CLI mode).

        Decorators are safe no-ops so definitions can be registered, but any
        attempt to use the SkillsProvider API (load_skill, read_skill_resource,
        run_skill_script, …) will raise a RuntimeError with installation
        guidance.
        """

        def __init__(self, *a: Any, **kw: Any) -> None:
            for k, v in kw.items():
                setattr(self, k, v)

        def resource(self, **kw: Any):  # noqa: ANN201
            def _dec(fn: Any) -> Any:
                return fn

            return _dec

        def script(self, **kw: Any):  # noqa: ANN201
            def _dec(fn: Any) -> Any:
                return fn

            return _dec

        # ------------------------------------------------------------------
        # SkillsProvider API stubs (raise clear errors when misused)
        # ------------------------------------------------------------------

        def load_skill(self, *args: Any, **kwargs: Any) -> Any:  # noqa: ANN201
            """Stub for SkillsProvider.load_skill when agent_framework is absent."""
            raise RuntimeError(
                "SkillsProvider.load_skill cannot be used because "
                "agent_framework is not installed. Install the agent_framework "
                "package to enable the full skills provider API."
            )

        def read_skill_resource(self, *args: Any, **kwargs: Any) -> Any:  # noqa: ANN201
            """Stub for SkillsProvider.read_skill_resource when agent_framework is absent."""
            raise RuntimeError(
                "SkillsProvider.read_skill_resource cannot be used because "
                "agent_framework is not installed. Install the agent_framework "
                "package to enable the full skills provider API."
            )

        def run_skill_script(self, *args: Any, **kwargs: Any) -> Any:  # noqa: ANN201
            """Stub for SkillsProvider.run_skill_script when agent_framework is absent."""
            raise RuntimeError(
                "SkillsProvider.run_skill_script cannot be used because "
                "agent_framework is not installed. Install the agent_framework "
                "package to enable the full skills provider API."
            )

    Skill = SkillResource = SkillScript = SkillsProvider = _Stub  # type: ignore[assignment,misc]

# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------

_SKILLS_DIR = Path(__file__).parent
_SKILL_DIR = _SKILLS_DIR / "meridian-code-review"
_REFS_DIR = _SKILL_DIR / "references"
_REPO_ROOT = _SKILLS_DIR.parent.parent


def _read(path: Path) -> str:
    """Read *path* as UTF-8 text; return empty string on any I/O error."""
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return ""


# ---------------------------------------------------------------------------
# Skill definition
# ---------------------------------------------------------------------------

mdc_code_review_skill = Skill(
    name="meridian-code-review",
    description=dedent("""\
        Code review and architecture compliance skill for the Meridian
        project — a .NET 9 / C# 13 market data system with WPF desktop app, F# 8.0
        domain models, real-time streaming pipelines, and tiered JSONL/Parquet
        storage. Use this skill whenever the user asks to review, audit, refactor,
        or improve C# or F# code from Meridian, or when they share
        .cs/.fs files and want feedback.
        Also trigger on: MVVM compliance, ViewModel extraction, code-behind cleanup,
        real-time performance, hot-path optimization, pipeline throughput, provider
        implementation review, backfill logic, data integrity validation, error
        handling patterns, test code quality, unit test review, ProviderSdk
        compliance, dependency violations, JSON source generator usage, hot config
        reload, or WPF architecture — even without naming the project. If code
        references Meridian namespaces, BindableBase, EventPipeline,
        IMarketDataClient, IStorageSink, or ProviderSdk types, use this skill.
    """),
    # Full skill instructions — identical to what is in SKILL.md so the
    # code-defined version is self-contained when the file is absent.
    content=_read(_SKILL_DIR / "SKILL.md"),
    resources=[
        # ------------------------------------------------------------------
        # Static reference resources bundled alongside the skill
        # ------------------------------------------------------------------
        SkillResource(
            name="architecture",
            description=(
                "Deep project context: solution layout (all 10 projects), "
                "expanded dependency graph, provider/backfill architecture, "
                "F# interop rules, testing conventions, and ADR quick-reference. "
                "Read when you need more detail than the SKILL.md summary."
            ),
            content=_read(_REFS_DIR / "architecture.md"),
        ),
        SkillResource(
            name="schemas",
            description=(
                "JSON schemas for evals.json, grading.json, benchmark.json, "
                "and timing.json. Read when generating or validating eval artifacts."
            ),
            content=_read(_REFS_DIR / "schemas.md"),
        ),
        SkillResource(
            name="grader",
            description=(
                "Assertions grader instructions for evaluating skill outputs against "
                "the test cases in evals.json. Read when grading eval runs."
            ),
            content=_read(_SKILL_DIR / "agents" / "grader.md"),
        ),
        SkillResource(
            name="evals",
            description=(
                "Eval set with 8 test cases and assertions for testing this skill. "
                "Read when running or inspecting skill evaluations."
            ),
            content=_read(_SKILL_DIR / "evals" / "evals.json"),
        ),
    ],
)

# ---------------------------------------------------------------------------
# AI Documentation Maintenance skill
# ---------------------------------------------------------------------------

ai_docs_maintain_skill = Skill(
    name="ai-docs-maintain",
    description=dedent("""\
        AI documentation maintenance skill for the Meridian project.
        Use this skill when the user asks to check AI doc freshness, detect drift
        between documentation and code, archive stale docs, validate cross-references,
        or generate a sync report for AI-related files. Also trigger when asked to
        "update AI docs", "check doc staleness", "archive deprecated docs", or
        "sync AI instructions".
    """),
    content=dedent("""\
        # AI Documentation Maintenance

        This skill maintains the health of AI assistant documentation in the
        Meridian repository.

        ## Available Commands

        Run via the `ai-docs-maintenance.py` script:

        ```bash
        # Check staleness of all AI docs
        python3 build/scripts/docs/ai-docs-maintenance.py freshness

        # Detect drift between docs and code reality
        python3 build/scripts/docs/ai-docs-maintenance.py drift

        # Preview stale docs for archiving
        python3 build/scripts/docs/ai-docs-maintenance.py archive-stale

        # Validate cross-references between AI docs
        python3 build/scripts/docs/ai-docs-maintenance.py validate-refs

        # Generate a full sync report (markdown)
        python3 build/scripts/docs/ai-docs-maintenance.py sync-report --output docs/generated/ai-docs-sync-report.md

        # Run all checks
        python3 build/scripts/docs/ai-docs-maintenance.py full --json-output /tmp/ai-docs-report.json
        ```

        Or via Makefile targets:

        ```bash
        make ai-docs-freshness      # Check AI doc freshness
        make ai-docs-drift          # Detect doc/code drift
        make ai-docs-sync-report    # Generate sync report
        make ai-docs-archive        # Preview archive candidates
        make ai-docs-archive-execute # Actually archive stale docs
        make ai-audit-ai-docs       # Integrated audit via ai-repo-updater
        ```

        ## Workflow

        1. Run `freshness` to find stale docs (>60 days warning, >120 days critical)
        2. Run `drift` to find where docs diverge from code (provider counts, workflow counts, file counts)
        3. Fix stale/drifted docs by updating content and timestamps
        4. Run `archive-stale` to identify deprecated content for archiving
        5. Run `validate-refs` to check for broken cross-references
        6. Generate a `sync-report` for human review

        ## Key Files

        - Script: `build/scripts/docs/ai-docs-maintenance.py`
        - Integrated auditor: `build/scripts/ai-repo-updater.py` (command: `audit-ai-docs`)
        - Master AI index: `docs/ai/README.md`
        - Root context: `CLAUDE.md`
    """),
    resources=[],
)


_AI_DOCS_SCRIPT = _REPO_ROOT / "build" / "scripts" / "docs" / "ai-docs-maintenance.py"


def _run_ai_docs_cmd(command: str, extra_args: list[str] | None = None,
                     timeout: int = 30) -> str:
    """Run an ai-docs-maintenance.py command and return output.

    Exit codes: 0 = clean, 1 = findings exist (still returns valid JSON),
    2 = script error.
    """
    cmd = [sys.executable, str(_AI_DOCS_SCRIPT), command]
    if extra_args:
        cmd.extend(extra_args)
    try:
        result = subprocess.run(
            cmd, capture_output=True, text=True,
            cwd=str(_REPO_ROOT), timeout=timeout,
        )
        # Exit code 0 (clean) and 1 (findings) both produce valid output
        if result.returncode <= 1:
            return result.stdout.strip() or result.stderr.strip()
        # Exit code 2 = script error
        return f"Error (exit {result.returncode}): {result.stderr.strip()}"
    except (subprocess.SubprocessError, OSError) as exc:
        return f"Error: {exc}"


@ai_docs_maintain_skill.resource(
    name="doc-health-summary",
    description=(
        "Live AI documentation health summary: stale doc count, drift items, "
        "and broken references. Refreshed on every read."
    ),
)
def doc_health_summary() -> Any:
    """Return current AI doc health status from the maintenance script."""
    import json as _json
    raw = _run_ai_docs_cmd("full", timeout=60)
    try:
        data = _json.loads(raw)
        s = data.get("summary", {})
        lines = [
            f"Stale docs    : {s.get('stale_docs', 0)}",
            f"Critical      : {s.get('critical', 0)}",
            f"Warnings      : {s.get('warning', 0)}",
            f"Drift items   : {s.get('drift_items', 0)}",
            f"Info          : {s.get('info', 0)}",
        ]
        return "\n".join(lines)
    except (_json.JSONDecodeError, KeyError):
        return raw


@ai_docs_maintain_skill.script(
    name="run-freshness",
    description="Check staleness of all AI documentation files. Returns JSON report.",
)
def run_freshness_script() -> str:
    """Execute ai-docs-maintenance.py freshness check."""
    return _run_ai_docs_cmd("freshness")


@ai_docs_maintain_skill.script(
    name="run-drift",
    description="Detect where AI documentation diverges from code reality. Returns JSON report.",
)
def run_drift_script() -> str:
    """Execute ai-docs-maintenance.py drift check."""
    return _run_ai_docs_cmd("drift")


@ai_docs_maintain_skill.script(
    name="run-full",
    description="Run all AI doc maintenance checks (freshness, drift, refs, archive). Returns JSON.",
)
def run_full_script() -> str:
    """Execute ai-docs-maintenance.py full check."""
    return _run_ai_docs_cmd("full", timeout=60)


@ai_docs_maintain_skill.script(
    name="run-archive",
    description=(
        "Find deprecated docs that should be archived. "
        "Set execute=True to actually move files (default: dry-run preview only)."
    ),
)
def run_archive_script(execute: bool = False) -> str:
    """Execute ai-docs-maintenance.py archive-stale with optional execution."""
    extra = ["--execute"] if execute else []
    return _run_ai_docs_cmd("archive-stale", extra_args=extra)


# ---------------------------------------------------------------------------
# Dynamic resources — re-evaluated on every read
# ---------------------------------------------------------------------------


@mdc_code_review_skill.resource(
    name="project-stats",
    description=(
        "Live project statistics: source file counts by language and test file "
        "count, derived from the current filesystem state. Refreshed on every read."
    ),
)
def project_stats() -> Any:
    """Return current source-file and test-file statistics for the repository."""
    repo_root = _REPO_ROOT

    def _count(directory: str, pattern: str) -> int:
        try:
            result = subprocess.run(
                [
                    "find",
                    directory,
                    "-name",
                    pattern,
                    "-not",
                    "-path",
                    "*/obj/*",
                    "-not",
                    "-path",
                    "*/bin/*",
                ],
                capture_output=True,
                text=True,
                timeout=10,
            )
            return len(result.stdout.strip().splitlines())
        except (subprocess.SubprocessError, subprocess.TimeoutExpired, OSError):
            return 0

    src = str(repo_root / "src")
    tests = str(repo_root / "tests")
    cs_src = _count(src, "*.cs")
    fs_src = _count(src, "*.fs")
    cs_tests = _count(tests, "*.cs")

    return dedent(f"""\
        Repository root : {repo_root}
        Source files (src/):
          C# (.cs) : {cs_src}
          F# (.fs) : {fs_src}
        Test files (tests/):
          C# (.cs) : {cs_tests}
    """)


@mdc_code_review_skill.resource(
    name="git-context",
    description=(
        "Current git branch, the latest commit touching src/ or tests/, and the "
        "list of files changed in that commit. Refreshed on every read."
    ),
)
def git_context() -> Any:
    """Return current git context: branch, last relevant commit, changed files."""
    repo_root = _REPO_ROOT

    def _git(*args: str) -> str:
        try:
            result = subprocess.run(
                ["git", *args],
                capture_output=True,
                text=True,
                cwd=str(repo_root),
                timeout=10,
            )
            return result.stdout.strip()
        except (subprocess.SubprocessError, subprocess.TimeoutExpired, OSError):
            return "(unavailable)"

    branch = _git("rev-parse", "--abbrev-ref", "HEAD")
    last_commit = _git(
        "log", "-1", "--pretty=format:%h %s (%cr)", "--", "src/", "tests/"
    )
    raw_changed = _git("diff", "--name-only", "HEAD~1", "HEAD", "--", "src/", "tests/")

    lines = [
        f"Branch      : {branch}",
        f"Last commit : {last_commit}",
    ]
    if raw_changed:
        changed_files = raw_changed.splitlines()
        lines.append(f"Changed files in last commit ({len(changed_files)}):")
        for f in changed_files[:20]:
            lines.append(f"  {f}")
        if len(changed_files) > 20:
            lines.append(f"  … and {len(changed_files) - 20} more")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Code-defined scripts — run in-process
# ---------------------------------------------------------------------------


@mdc_code_review_skill.script(
    name="validate-skill",
    description=(
        "Validate the meridian-code-review SKILL.md frontmatter and directory "
        "structure. Returns 'OK' on success or a failure message."
    ),
)
def validate_skill_script() -> str:
    """Validate the skill definition via quick_validate.py."""
    # Import the validation helper from the skill's scripts package.
    # We add the meridian-code-review directory to sys.path so that the
    # ``from scripts.quick_validate import …`` import inside package_skill.py
    # keeps working too.
    _scripts_parent = str(_SKILL_DIR)
    if _scripts_parent not in sys.path:
        sys.path.insert(0, _scripts_parent)

    try:
        from scripts.quick_validate import validate_skill  # type: ignore[import]

        valid, message = validate_skill(str(_SKILL_DIR))
        return message
    except ImportError:
        # Fallback: minimal structural check without PyYAML.
        skill_md = _SKILL_DIR / "SKILL.md"
        if not skill_md.exists():
            return "FAIL: SKILL.md not found"
        content = skill_md.read_text(encoding="utf-8")
        if not content.startswith("---"):
            return "FAIL: SKILL.md is missing YAML frontmatter"
        return "OK: SKILL.md exists and starts with frontmatter"


@mdc_code_review_skill.script(
    name="run-eval",
    description=(
        "Run the trigger-evaluation suite (evals/evals.json) to measure how "
        "reliably the skill description causes Claude to invoke this skill. "
        "Accepts optional ``description`` (override the description under test) "
        "and ``runs_per_query`` (default 3)."
    ),
)
def run_eval_script(description: str = "", runs_per_query: int = 3) -> str:
    """Execute run_eval.py for the bundled eval set."""
    evals_file = _SKILL_DIR / "evals" / "evals.json"

    cmd = [
        sys.executable,
        "-m",
        "scripts.run_eval",
        "--eval-set",
        str(evals_file),
        "--skill-path",
        str(_SKILL_DIR),
        "--runs-per-query",
        str(runs_per_query),
        "--verbose",
    ]
    if description:
        cmd.extend(["--description", description])

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=str(_SKILL_DIR),  # scripts/ is a package relative to meridian-code-review/
            timeout=300,
        )
        output = result.stdout.strip()
        if result.returncode != 0 and not output:
            return f"Error (exit {result.returncode}): {result.stderr.strip()}"
        return output
    except subprocess.TimeoutExpired:
        return "Error: eval timed out after 300 s"
    except Exception as exc:
        return f"Error: {exc}"


@mdc_code_review_skill.script(
    name="aggregate-benchmark",
    description=(
        "Aggregate grading results from a workspace directory into "
        "benchmark.json and benchmark.md summary files. "
        "Requires ``workspace`` (path to the benchmark directory) and an "
        "optional ``skill_name`` override (default: meridian-code-review)."
    ),
)
def aggregate_benchmark_script(workspace: str, skill_name: str = "meridian-code-review") -> str:
    """Execute aggregate_benchmark.py for a given workspace directory."""
    cmd = [
        sys.executable,
        "-m",
        "scripts.aggregate_benchmark",
        workspace,
        "--skill-name",
        skill_name,
    ]

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=str(_SKILL_DIR),
            timeout=60,
        )
        output = result.stdout.strip()
        if result.returncode != 0 and not output:
            return f"Error (exit {result.returncode}): {result.stderr.strip()}"
        return output or "Done."
    except subprocess.TimeoutExpired:
        return "Error: aggregate-benchmark timed out after 60 s"
    except Exception as exc:
        return f"Error: {exc}"


# ---------------------------------------------------------------------------
# Script runner for file-based scripts (subprocess execution)
# ---------------------------------------------------------------------------


def _skill_script_runner(
    skill: Skill, script: SkillScript, args: dict[str, Any] | None = None
) -> str:
    """Execute a file-based skill script as a Python subprocess.

    The runner is intentionally minimal: it validates that the script file
    exists, builds a ``python <script_path> [--key value …]`` command from
    the *args* dict, and captures stdout.  It does **not** use a shell, so
    no shell expansion or injection is possible.

    Parameters
    ----------
    skill:
        The resolved :class:`Skill` whose ``path`` attribute points to the
        skill directory on disk.
    script:
        The :class:`SkillScript` being executed; its ``path`` attribute is
        relative to the skill directory.
    args:
        Optional mapping of argument name → value.  Each entry is appended
        as ``--<name> <value>`` (``None`` values are skipped).
    """
    if skill.path is None:
        raise ValueError(
            f"Skill '{skill.name}' has no filesystem path — "
            "cannot execute a file-based script"
        )

    script_path = Path(skill.path) / script.path
    if not script_path.exists():
        raise FileNotFoundError(f"Script not found: {script_path}")

    cmd: list[str] = [sys.executable, str(script_path)]
    if args:
        for key, value in args.items():
            if value is not None:
                cmd.extend([f"--{key}", str(value)])

    result = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        cwd=str(_REPO_ROOT),
        timeout=300,
    )

    if result.returncode != 0:
        raise RuntimeError(
            f"Script exited with code {result.returncode}:\n{result.stderr.strip()}"
        )

    return result.stdout.strip()


# ---------------------------------------------------------------------------
# Skills provider
# ---------------------------------------------------------------------------

_BLUEPRINT_SKILL_DIR = _SKILLS_DIR / "meridian-blueprint"
_BLUEPRINT_REFS_DIR = _BLUEPRINT_SKILL_DIR / "references"

mdc_blueprint_skill = Skill(
    name="meridian-blueprint",
    description=(
        "Blueprint Mode skill for the Meridian project. "
        "Translates a single prioritized idea into a complete, code-ready technical "
        "design document — interfaces, component designs, data flows, XAML sketches, "
        "test plans, and implementation checklists — grounded in Meridian's actual stack: "
        "C# 13, F# 8, .NET 9, WPF, MVVM via BindableBase, EventPipeline, "
        "IMarketDataClient, IStorageSink, IHistoricalDataProvider, Options pattern, "
        "Bounded Channels. "
        "Trigger on: \"blueprint\", \"design document\", \"technical spec\", "
        "\"design the\", \"architect the\", \"what interfaces do we need for\", "
        "\"spike plan for\", \"interface-only design for\", or when a Roadmap/"
        "Brainstorm output needs to be turned into something a developer can implement "
        "tomorrow. Also trigger when the user says \"blueprint mode\" or provides an "
        "idea card from the Brainstorm or Idea Evaluator pipeline stage."
    ),
    # Full skill instructions — identical to SKILL.md so the code-defined version
    # is self-contained when the file-based version is absent.
    content=_read(_BLUEPRINT_SKILL_DIR / "SKILL.md"),
    resources=[
        SkillResource(
            name="blueprint-patterns",
            description=(
                "Meridian interface patterns, naming conventions, ADR contract reference, "
                "DI registration patterns, Options pattern, Channel/pipeline pattern, "
                "WPF/MVVM patterns, F# domain type patterns, error handling and "
                "structured logging patterns, storage sink pattern, historical provider "
                "pattern, and breaking change checklist. "
                "Read when naming interfaces, designing components, or checking ADR compliance."
            ),
            content=_read(_BLUEPRINT_REFS_DIR / "blueprint-patterns.md"),
        ),
        SkillResource(
            name="pipeline-position",
            description=(
                "Full pipeline diagram (Brainstorm → Evaluator → Roadmap → Blueprint → "
                "Implementation → Code Review → Test Writing), stage input/output contracts, "
                "handoff contracts between stages, and stage bypass rules. "
                "Read when determining where a blueprint request fits in the pipeline or "
                "when handing off to another skill."
            ),
            content=_read(_BLUEPRINT_REFS_DIR / "pipeline-position.md"),
        ),
    ],
)


@mdc_blueprint_skill.resource(
    name="blueprint-git-context",
    description=(
        "Current git branch, the latest commit touching .claude/skills/meridian-blueprint/ "
        "or .github/agents/meridian-blueprint-agent.md, and any pending changes to those "
        "paths. Refreshed on every read."
    ),
)
def blueprint_git_context() -> Any:
    """Return current git context scoped to the blueprint skill files."""
    repo_root = _REPO_ROOT

    def _git(*args: str) -> str:
        try:
            result = subprocess.run(
                ["git", *args],
                capture_output=True,
                text=True,
                cwd=str(repo_root),
                timeout=10,
            )
            return result.stdout.strip()
        except (subprocess.SubprocessError, subprocess.TimeoutExpired, OSError):
            return "(unavailable)"

    branch = _git("rev-parse", "--abbrev-ref", "HEAD")
    last_commit = _git(
        "log",
        "-1",
        "--pretty=format:%h %s (%cr)",
        "--",
        ".claude/skills/meridian-blueprint/",
        ".github/agents/meridian-blueprint-agent.md",
        ".claude/agents/meridian-blueprint.md",
    )
    pending = _git(
        "diff",
        "--name-only",
        "--",
        ".claude/skills/meridian-blueprint/",
        ".github/agents/meridian-blueprint-agent.md",
        ".claude/agents/meridian-blueprint.md",
    )

    lines = [
        f"Branch              : {branch}",
        f"Last blueprint commit: {last_commit or '(none yet)'}",
    ]
    if pending:
        lines.append("Pending changes:")
        for f in pending.splitlines():
            lines.append(f"  {f}")

    return "\n".join(lines)


@mdc_blueprint_skill.script(
    name="validate-skill",
    description=(
        "Validate the meridian-blueprint SKILL.md frontmatter and directory structure. "
        "Returns 'OK' on success or a failure message."
    ),
)
def blueprint_validate_skill_script() -> str:
    """Validate the blueprint skill definition with a minimal structural check."""
    skill_md = _BLUEPRINT_SKILL_DIR / "SKILL.md"
    if not skill_md.exists():
        return "FAIL: SKILL.md not found"
    content = skill_md.read_text(encoding="utf-8")
    if not content.startswith("---"):
        return "FAIL: SKILL.md is missing YAML frontmatter"

    missing_refs = []
    for ref_file in ("blueprint-patterns.md", "pipeline-position.md"):
        if not (_BLUEPRINT_REFS_DIR / ref_file).exists():
            missing_refs.append(ref_file)
    if missing_refs:
        return f"FAIL: Missing reference files: {', '.join(missing_refs)}"

    return "OK: meridian-blueprint SKILL.md exists, has frontmatter, and all reference files present"


skills_provider = SkillsProvider(
    # Discover file-based skills from the skills/ directory.
    # File-based skills take precedence over code-defined ones with the
    # same name, so the Skill instances above serve as self-contained
    # fallbacks and as hosts for dynamic resources and in-process scripts.
    skill_paths=_SKILLS_DIR,
    # Register the code-defined skills.
    skills=[mdc_code_review_skill, ai_docs_maintain_skill, mdc_blueprint_skill],
    # Provide a runner for any file-based .py scripts that may be added to
    # skill directories in the future.
    script_runner=_skill_script_runner,
)


# ---------------------------------------------------------------------------
# Execution metrics
# ---------------------------------------------------------------------------

_logger = _logging.getLogger(__name__)

#: Output prefixes that indicate a script failure reported via return value
#: rather than an exception.  ``_timed_call`` checks for these so that
#: automation never silently succeeds when a script signals an error.
_SCRIPT_ERROR_PREFIXES: tuple[str, ...] = ("Error: ", "Error (exit", "FAIL: ")


@_dataclasses.dataclass
class ExecutionRecord:
    """Record of a single skill script execution with timing and outcome."""

    skill: str
    script: str
    started_at: str
    duration_ms: int
    success: bool
    output_preview: str
    error: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Return a plain dict representation suitable for JSON serialisation."""
        return _dataclasses.asdict(self)


def _timed_call(
    skill_name: str,
    script_name: str,
    fn: Any,
    *args: Any,
    **kwargs: Any,
) -> tuple[str, ExecutionRecord]:
    """Call *fn*, capturing wall-clock time and success/failure.

    Returns a ``(output, record)`` tuple where *output* is the string
    returned by *fn* and *record* is an :class:`ExecutionRecord` with
    timing metadata.  Exceptions are caught and stored in the record
    instead of propagating to the caller.
    """
    t0 = _time.monotonic()
    started_at = _dt.datetime.now(_dt.UTC).isoformat(timespec="seconds").replace("+00:00", "Z")
    error: str | None = None
    output = ""
    try:
        output = fn(*args, **kwargs) or ""
        # Several scripts signal failure by returning an error-prefixed string
        # rather than raising.  Treat those outputs as failures so that chains
        # and automation never silently succeed on a script that reported an
        # error.
        if any(output.startswith(p) for p in _SCRIPT_ERROR_PREFIXES):
            success = False
            error = output
            _logger.error("Script %s/%s reported failure: %s", skill_name, script_name, output)
        else:
            success = True
    except Exception as exc:
        error = str(exc)
        success = False
        _logger.error("Script %s/%s failed: %s", skill_name, script_name, exc)
    duration_ms = int((_time.monotonic() - t0) * 1000)
    record = ExecutionRecord(
        skill=skill_name,
        script=script_name,
        started_at=started_at,
        duration_ms=duration_ms,
        success=success,
        output_preview=output[:200],
        error=error,
    )
    _logger.info(
        "Executed %s/%s in %d ms (success=%s)",
        skill_name,
        script_name,
        duration_ms,
        success,
    )
    return output, record


# ---------------------------------------------------------------------------
# Task chaining — predefined multi-step workflows
# ---------------------------------------------------------------------------

#: Named chains per skill: { skill_name: { chain_name: [script_name, …] } }
_SKILL_CHAINS: dict[str, dict[str, list[str]]] = {
    "meridian-code-review": {
        "validate-and-eval": ["validate-skill", "run-eval"],
        "full-check": ["validate-skill", "run-eval", "aggregate-benchmark"],
    },
    "ai-docs-maintain": {
        "health-check": ["run-freshness", "run-drift"],
        "full-health-check": ["run-freshness", "run-drift", "run-full"],
        "archive-workflow": ["run-freshness", "run-archive"],
    },
}

#: Script callables keyed by (skill_name, script_name) — used by the CLI and
#: :func:`run_skill_chain` to look up the right function without going through
#: the SkillsProvider API.
_SCRIPT_REGISTRY: dict[tuple[str, str], Any] = {
    ("meridian-code-review", "validate-skill"): validate_skill_script,
    ("meridian-code-review", "run-eval"): run_eval_script,
    ("meridian-code-review", "aggregate-benchmark"): aggregate_benchmark_script,
    ("ai-docs-maintain", "run-freshness"): run_freshness_script,
    ("ai-docs-maintain", "run-drift"): run_drift_script,
    ("ai-docs-maintain", "run-full"): run_full_script,
    ("ai-docs-maintain", "run-archive"): run_archive_script,
}

#: Resource callables keyed by (skill_name, resource_name).
_RESOURCE_REGISTRY: dict[tuple[str, str], Any] = {
    ("meridian-code-review", "project-stats"): project_stats,
    ("meridian-code-review", "git-context"): git_context,
    ("ai-docs-maintain", "doc-health-summary"): doc_health_summary,
}


class _SkillResources(TypedDict):
    """Typed structure for the per-skill resource map used by the CLI."""

    static: list[str]
    dynamic: list[str]


def _scripts_from_registry() -> dict[str, list[str]]:
    """Derive the per-skill script listing from :data:`_SCRIPT_REGISTRY`.

    Using the registry as the single source of truth prevents the CLI's
    ``list-scripts`` output from drifting out of sync when new
    ``@*.script(…)`` entries are added.
    """
    result: dict[str, list[str]] = {}
    for skill, script in _SCRIPT_REGISTRY:
        result.setdefault(skill, []).append(script)
    return result


#: The skill name that "owns" the static (file-based) resources listed below.
#: Centralised here so that :func:`_resources_from_registries` and
#: :meth:`SkillsProviderCli._cmd_read_resource` share a single constant rather
#: than duplicating the literal string ``"meridian-code-review"``.
_STATIC_RESOURCE_SKILL: str = "meridian-code-review"

#: Static resource → file path.  This is the single source of truth for
#: file-backed resources; :func:`_resources_from_registries` reads its keys to
#: populate the ``list-resources`` output automatically.
_STATIC_RESOURCE_PATHS: dict[str, Path] = {
    "architecture": _REFS_DIR / "architecture.md",
    "schemas": _REFS_DIR / "schemas.md",
    "grader": _SKILL_DIR / "agents" / "grader.md",
    "evals": _SKILL_DIR / "evals" / "evals.json",
}


def _resources_from_registries() -> dict[str, _SkillResources]:
    """Derive per-skill resource listing from :data:`_RESOURCE_REGISTRY` and
    :data:`_STATIC_RESOURCE_PATHS`.

    Using the registries and static-path map as the single source of truth
    prevents the CLI's ``list-resources`` output from drifting when new
    resources are added — no extra dict needs updating.
    """
    result: dict[str, _SkillResources] = {}
    # Static (file-based) resources are all owned by _STATIC_RESOURCE_SKILL
    if _STATIC_RESOURCE_PATHS:
        entry = result.setdefault(
            _STATIC_RESOURCE_SKILL, _SkillResources(static=[], dynamic=[])
        )
        entry["static"] = list(_STATIC_RESOURCE_PATHS)
    # Dynamic resources from the registry
    for skill, resource in _RESOURCE_REGISTRY:
        entry = result.setdefault(skill, _SkillResources(static=[], dynamic=[]))
        entry["dynamic"].append(resource)
    return result


def _all_skill_names() -> list[str]:
    """Return the sorted union of skill names from all registries.

    Includes skills from :data:`_SCRIPT_REGISTRY`, :data:`_RESOURCE_REGISTRY`,
    :data:`_STATIC_RESOURCE_SKILL`, and file-based packages discovered under
    ``.claude/skills`` so that portable skills without code-defined companions
    still appear in list output. Used by the CLI's list-* commands so that the
    skill set is always derived from a single source of truth rather than being
    repeated in every error-message path.
    """
    return sorted(
        set(discover_skills(_SKILLS_DIR))
        | {skill for skill, _ in _SCRIPT_REGISTRY}
        | {skill for skill, _ in _RESOURCE_REGISTRY}
        | {_STATIC_RESOURCE_SKILL}
    )


def run_skill_chain(
    skill_name: str,
    scripts: list[str],
    params: dict[str, dict[str, Any]] | None = None,
    stop_on_error: bool = True,
) -> list[ExecutionRecord]:
    """Run *scripts* sequentially for *skill_name*.

    Parameters
    ----------
    skill_name:
        Name of the skill whose scripts should be executed.
    scripts:
        Ordered list of script names to run.
    params:
        Optional mapping of ``{script_name: {param_name: value}}`` for
        scripts that accept keyword arguments.
    stop_on_error:
        When *True* (default) the chain halts at the first failing script.
        When *False* all scripts are attempted regardless of failures.

    Returns
    -------
    list[ExecutionRecord]
        One record per attempted step, in execution order.
    """
    params = params or {}
    records: list[ExecutionRecord] = []
    for script_name in scripts:
        fn = _SCRIPT_REGISTRY.get((skill_name, script_name))
        if fn is None:
            records.append(
                ExecutionRecord(
                    skill=skill_name,
                    script=script_name,
                    started_at=_dt.datetime.utcnow().isoformat(timespec="seconds") + "Z",
                    duration_ms=0,
                    success=False,
                    output_preview="",
                    error=f"Unknown script '{script_name}' for skill '{skill_name}'",
                )
            )
            if stop_on_error:
                break
            continue
        script_params = params.get(script_name, {})
        _output, record = _timed_call(skill_name, script_name, fn, **script_params)
        records.append(record)
        if not record.success and stop_on_error:
            break
    return records


# ---------------------------------------------------------------------------
# Dynamic skill discovery
# ---------------------------------------------------------------------------


def discover_skills(search_root: Path | None = None) -> list[str]:
    """Scan *search_root* for ``SKILL.md`` files and return skill names.

    Defaults to ``_SKILLS_DIR`` when *search_root* is *None*.  The skill
    name is read from the ``name:`` field in the YAML frontmatter; the
    containing directory name is used as a fallback when the frontmatter
    cannot be parsed.
    """
    root = search_root or _SKILLS_DIR
    found: list[str] = []
    for skill_md in sorted(root.rglob("SKILL.md")):
        name = _parse_skill_name(skill_md) or skill_md.parent.name
        found.append(name)
    return found


def _parse_skill_name(skill_md: Path) -> str | None:
    """Extract ``name:`` from a SKILL.md YAML frontmatter block.

    Returns *None* when the file cannot be read or has no ``name:`` entry.
    """
    try:
        content = skill_md.read_text(encoding="utf-8")
    except OSError:
        return None
    if not content.startswith("---"):
        return None
    parts = content.split("---", 2)
    if len(parts) < 3:
        return None
    for line in parts[1].splitlines():
        if line.startswith("name:"):
            return line.split(":", 1)[1].strip()
    return None


# ---------------------------------------------------------------------------
# Command-line interface
# ---------------------------------------------------------------------------


class SkillsProviderCli:
    """Unified CLI for interacting with the skills provider.

    Provides commands to list skills, read resources, run individual scripts,
    chain multiple steps together, and discover new skills from the filesystem.

    Invoke directly::

        python3 .claude/skills/skills_provider.py --help
        python3 .claude/skills/skills_provider.py list
        python3 .claude/skills/skills_provider.py run-script meridian-code-review validate-skill
        python3 .claude/skills/skills_provider.py chain meridian-code-review validate-skill run-eval
        python3 .claude/skills/skills_provider.py run-chain ai-docs-maintain full-health-check
    """

    # ------------------------------------------------------------------
    # Metadata tables (used by list-* commands)
    # ------------------------------------------------------------------

    _SKILL_DESCRIPTIONS: dict[str, str] = {
        "meridian-code-review": (
            "Code review and architecture compliance for the Meridian project."
        ),
        "meridian-simulated-user-panel": (
            "Manifest-driven design-partner, release-gate, and usability-lab user panels."
        ),
        "ai-docs-maintain": (
            "AI documentation maintenance: freshness checks, drift detection, archiving."
        ),
    }

    #: Derived from :data:`_RESOURCE_REGISTRY` and :data:`_STATIC_RESOURCE_PATHS`
    #: — do not edit by hand.  Adding a new dynamic resource to
    #: ``_RESOURCE_REGISTRY`` or a new static path to ``_STATIC_RESOURCE_PATHS``
    #: will automatically appear in ``list-resources`` output.
    _RESOURCES: dict[str, _SkillResources] = _resources_from_registries()

    #: Derived from :data:`_SCRIPT_REGISTRY` — do not edit by hand.
    #: Adding a new ``@*.script(…)`` to ``_SCRIPT_REGISTRY`` will automatically
    #: appear in ``list-scripts`` output without any further change here.
    _SCRIPTS: dict[str, list[str]] = _scripts_from_registry()

    def __init__(self) -> None:
        self._parser = self._build_parser()

    # ------------------------------------------------------------------
    # Argument parser
    # ------------------------------------------------------------------

    @staticmethod
    def _build_parser() -> _argparse.ArgumentParser:
        p = _argparse.ArgumentParser(
            prog="skills_provider.py",
            description=(
                "Meridian skills provider CLI.\n\n"
                "Manage, inspect, and execute skill scripts and resources "
                "directly from the command line."
            ),
            formatter_class=_argparse.RawDescriptionHelpFormatter,
        )
        sub = p.add_subparsers(dest="command", metavar="COMMAND")

        # list
        sub.add_parser("list", help="List all registered skill names and descriptions")

        # list-resources
        lr = sub.add_parser("list-resources", help="List resources available for a skill")
        lr.add_argument("skill", help="Skill name (e.g. meridian-code-review)")

        # list-scripts
        ls = sub.add_parser("list-scripts", help="List scripts available for a skill")
        ls.add_argument("skill", help="Skill name")

        # list-chains
        lc = sub.add_parser(
            "list-chains",
            help="List predefined task chains; omit SKILL to list all",
        )
        lc.add_argument("skill", nargs="?", help="Skill name (optional)")

        # read-resource
        rr = sub.add_parser(
            "read-resource", help="Read and print a skill resource (static or dynamic)"
        )
        rr.add_argument("skill", help="Skill name")
        rr.add_argument(
            "resource",
            help="Resource name (e.g. project-stats, git-context, architecture)",
        )

        # run-script
        rs = sub.add_parser("run-script", help="Execute a skill script and print output")
        rs.add_argument("skill", help="Skill name")
        rs.add_argument("script", help="Script name")
        rs.add_argument(
            "--param",
            action="append",
            dest="params",
            metavar="KEY=VALUE",
            help=(
                "Script parameter (repeatable). "
                "Example: --param runs_per_query=5 --param workspace=/tmp/bench"
            ),
        )
        rs.add_argument(
            "--json",
            action="store_true",
            dest="as_json",
            help="Wrap output and timing in an ExecutionRecord JSON envelope",
        )

        # chain
        ch = sub.add_parser(
            "chain", help="Run multiple scripts for a skill sequentially"
        )
        ch.add_argument("skill", help="Skill name")
        ch.add_argument("scripts", nargs="+", help="Script names to run in order")
        ch.add_argument(
            "--no-stop-on-error",
            action="store_true",
            help="Continue even if a script fails (default: stop at first error)",
        )
        ch.add_argument(
            "--param",
            action="append",
            dest="params",
            metavar="SCRIPT:KEY=VALUE",
            help=(
                "Script-specific parameter (repeatable). "
                "Example: --param run-eval:runs_per_query=5 "
                "--param aggregate-benchmark:workspace=/tmp/bench"
            ),
        )

        # run-chain
        rc = sub.add_parser(
            "run-chain", help="Execute a predefined named chain (see list-chains)"
        )
        rc.add_argument("skill", help="Skill name")
        rc.add_argument("chain", help="Chain name")
        rc.add_argument(
            "--no-stop-on-error",
            action="store_true",
            help="Continue even if a step fails",
        )

        # discover
        dsc = sub.add_parser(
            "discover", help="Scan the filesystem for SKILL.md files"
        )
        dsc.add_argument(
            "--root",
            metavar="DIR",
            default=None,
            help="Directory to scan (default: .claude/skills/)",
        )

        return p

    # ------------------------------------------------------------------
    # Command handlers
    # ------------------------------------------------------------------

    def _cmd_list(self) -> int:
        # Derive the complete set of skill names from the registries so that
        # newly added skills appear automatically without updating _SKILL_DESCRIPTIONS.
        print("Registered skills:\n")
        for name in _all_skill_names():
            desc = self._SKILL_DESCRIPTIONS.get(name, "(no description)")
            print(f"  {name}")
            print(f"    {desc}\n")
        return 0

    def _cmd_list_resources(self, skill: str) -> int:
        if skill not in self._RESOURCES:
            print(f"Unknown skill: '{skill}'. Available: {', '.join(_all_skill_names())}")
            return 1
        r = self._RESOURCES[skill]
        print(f"Resources for '{skill}':\n")
        if r["static"]:
            print("  Static (bundled from files):")
            for name in r["static"]:
                print(f"    {name}")
        if r["dynamic"]:
            print("  Dynamic (re-evaluated on every read):")
            for name in r["dynamic"]:
                print(f"    {name}")
        return 0

    def _cmd_list_scripts(self, skill: str) -> int:
        if skill not in self._SCRIPTS:
            print(f"Unknown skill: '{skill}'. Available: {', '.join(_all_skill_names())}")
            return 1
        print(f"Scripts for '{skill}':\n")
        for name in self._SCRIPTS[skill]:
            print(f"  {name}")
        return 0

    def _cmd_list_chains(self, skill: str | None) -> int:
        targets = (
            _SKILL_CHAINS
            if skill is None
            else {skill: _SKILL_CHAINS.get(skill, {})}
        )
        if skill and skill not in _SKILL_CHAINS:
            print(f"No chains defined for skill: '{skill}'")
            return 1
        for sname, chains in targets.items():
            print(f"Chains for '{sname}':")
            for cname, steps in chains.items():
                print(f"  {cname}: {' -> '.join(steps)}")
            print()
        return 0

    def _cmd_read_resource(self, skill: str, resource: str) -> int:
        fn = _RESOURCE_REGISTRY.get((skill, resource))
        if fn is not None:
            output, record = _timed_call(skill, resource, fn)
            print(output)
            _logger.debug("Resource metrics: %s", record.to_dict())
            return 0
        # Fallback: static file-based resources (keyed by _STATIC_RESOURCE_SKILL)
        if skill == _STATIC_RESOURCE_SKILL and resource in _STATIC_RESOURCE_PATHS:
            content = _read(_STATIC_RESOURCE_PATHS[resource])
            if content:
                print(content)
                return 0
            print(f"Resource file not found: {_STATIC_RESOURCE_PATHS[resource]}")
            return 1
        all_resources = (
            self._RESOURCES.get(skill, {}).get("static", [])
            + self._RESOURCES.get(skill, {}).get("dynamic", [])
        )
        print(
            f"Unknown resource '{resource}' for skill '{skill}'. "
            f"Available: {', '.join(all_resources) or '(none)'}"
        )
        return 1

    @staticmethod
    def _parse_params(raw: list[str] | None) -> dict[str, Any]:
        """Parse ``--param KEY=VALUE`` entries into a plain dict.

        Booleans (``true``/``false``) and integers are coerced to native types.
        """
        result: dict[str, Any] = {}
        for item in raw or []:
            if "=" not in item:
                print(
                    f"Invalid --param value {item!r}: expected KEY=VALUE format.",
                    file=sys.stderr,
                )
                raise SystemExit(1)
            k, _, v = item.partition("=")
            k = k.strip()
            if v.lower() == "true":
                result[k] = True
            elif v.lower() == "false":
                result[k] = False
            elif v.lstrip("-").isdigit():
                result[k] = int(v)
            else:
                result[k] = v
        return result

    @staticmethod
    def _parse_chain_params(raw: list[str] | None) -> dict[str, dict[str, Any]]:
        """Parse ``--param SCRIPT:KEY=VALUE`` into ``{script: {key: value}}``.

        Booleans and integers are coerced as in :meth:`_parse_params`.
        """
        result: dict[str, dict[str, Any]] = {}
        for item in raw or []:
            if ":" not in item or "=" not in item:
                print(
                    f"Invalid --param value {item!r}: expected SCRIPT:KEY=VALUE format.",
                    file=sys.stderr,
                )
                raise SystemExit(1)
            script, _, rest = item.partition(":")
            k, _, v = rest.partition("=")
            script, k = script.strip(), k.strip()
            params = result.setdefault(script, {})
            if v.lower() == "true":
                params[k] = True
            elif v.lower() == "false":
                params[k] = False
            elif v.lstrip("-").isdigit():
                params[k] = int(v)
            else:
                params[k] = v
        return result

    @staticmethod
    def _is_error_output(output: Any) -> bool:
        """Heuristically detect scripts that signal failure via their output string.

        Treats outputs starting with ``"Error:"``, ``"Error (exit"`` or ``"FAIL:"``
        (case-insensitive, ignoring leading whitespace) as failures.
        """
        if not isinstance(output, str):
            return False
        stripped = output.lstrip()
        lowered = stripped.lower()
        if lowered.startswith("error:"):
            return True
        if lowered.startswith("error (exit"):
            return True
        if lowered.startswith("fail:"):
            return True
        return False

    def _cmd_run_script(
        self,
        skill: str,
        script: str,
        params: dict[str, Any],
        as_json: bool,
    ) -> int:
        fn = _SCRIPT_REGISTRY.get((skill, script))
        if fn is None:
            available = ", ".join(self._SCRIPTS.get(skill, []))
            print(
                f"Unknown script '{script}' for skill '{skill}'. "
                f"Available: {available or '(none)'}"
            )
            return 1
        output, record = _timed_call(skill, script, fn, **params)
        # Combine timed-call success with heuristic detection of error-signalling output.
        success = record.success and not self._is_error_output(output)
        if as_json:
            print(
                _json_mod.dumps(
                    {"output": output, "metrics": record.to_dict()}, indent=2
                )
            )
        else:
            print(output)
            status = "OK" if success else "FAIL"
            print(f"\n[{record.duration_ms} ms | {status}]", file=sys.stderr)
        return 0 if success else 1

    def _cmd_chain(
        self,
        skill: str,
        scripts: list[str],
        stop_on_error: bool,
        params: dict[str, dict[str, Any]],
    ) -> int:
        records = run_skill_chain(
            skill, scripts, params=params, stop_on_error=stop_on_error
        )
        total = len(records)
        passed = sum(1 for r in records if r.success)
        for i, r in enumerate(records, 1):
            status = "OK  " if r.success else "FAIL"
            print(f"[{i}/{total}] {status}  {r.script}  ({r.duration_ms} ms)")
            if r.error:
                print(f"       Error: {r.error}")
        print(f"\nChain result: {passed}/{total} scripts succeeded")
        return 0 if passed == total else 1

    def _cmd_run_chain(self, skill: str, chain_name: str, stop_on_error: bool) -> int:
        skill_chains = _SKILL_CHAINS.get(skill)
        if not skill_chains:
            print(f"No chains defined for skill: '{skill}'")
            return 1
        scripts = skill_chains.get(chain_name)
        if not scripts:
            available = ", ".join(skill_chains)
            print(
                f"Unknown chain '{chain_name}' for skill '{skill}'. "
                f"Available: {available}"
            )
            return 1
        return self._cmd_chain(skill, scripts, stop_on_error, params={})

    def _cmd_discover(self, root: str | None) -> int:
        search = Path(root) if root else None
        found = discover_skills(search)
        if not found:
            print("No skills found.")
            return 0
        print(f"Discovered {len(found)} skill(s):\n")
        for name in found:
            print(f"  {name}")
        return 0

    # ------------------------------------------------------------------
    # Entry point
    # ------------------------------------------------------------------

    def run(self, argv: list[str] | None = None) -> int:
        """Parse *argv* and dispatch to the appropriate command handler."""
        args = self._parser.parse_args(argv)
        if args.command is None:
            self._parser.print_help()
            return 0
        if args.command == "list":
            return self._cmd_list()
        if args.command == "list-resources":
            return self._cmd_list_resources(args.skill)
        if args.command == "list-scripts":
            return self._cmd_list_scripts(args.skill)
        if args.command == "list-chains":
            return self._cmd_list_chains(getattr(args, "skill", None))
        if args.command == "read-resource":
            return self._cmd_read_resource(args.skill, args.resource)
        if args.command == "run-script":
            params = self._parse_params(args.params)
            return self._cmd_run_script(args.skill, args.script, params, args.as_json)
        if args.command == "chain":
            chain_params = self._parse_chain_params(args.params)
            return self._cmd_chain(
                args.skill,
                args.scripts,
                stop_on_error=not args.no_stop_on_error,
                params=chain_params,
            )
        if args.command == "run-chain":
            return self._cmd_run_chain(
                args.skill, args.chain, stop_on_error=not args.no_stop_on_error
            )
        if args.command == "discover":
            return self._cmd_discover(getattr(args, "root", None))
        self._parser.print_help()
        return 0


def main(argv: list[str] | None = None) -> None:
    """Entry point for the standalone CLI.

    Configures basic logging (warnings and above to stderr) then delegates
    to :class:`SkillsProviderCli`.
    """
    _logging.basicConfig(
        level=_logging.WARNING,
        format="%(levelname)s %(name)s: %(message)s",
    )
    raise SystemExit(SkillsProviderCli().run(argv))


if __name__ == "__main__":
    main()
