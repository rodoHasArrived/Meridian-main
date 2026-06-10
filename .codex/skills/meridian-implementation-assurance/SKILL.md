---
name: meridian-implementation-assurance
description: Implement Meridian changes with built-in correctness checks, performance guardrails, documentation synchronization, and structured self-evaluation. Use when Codex is asked to build or refactor code and must also verify behavior, prevent performance regressions, update existing docs, or add new docs in the correct repository section when none exists.
---

# Meridian Implementation Assurance

Deliver production-ready code changes and leave documentation in a consistent, current state.

> **GitHub Copilot equivalent:** [`.github/agents/implementation-assurance-agent.md`](../../../.github/agents/implementation-assurance-agent.md)
> **Claude Code equivalent:** [`.claude/skills/meridian-implementation-assurance/SKILL.md`](../../../.claude/skills/meridian-implementation-assurance/SKILL.md)
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before coding.
Read [`docs/ai/codex/self-improving-agents.md`](../../../docs/ai/codex/self-improving-agents.md)
before improving prompts, skills, agent profiles, eval rubrics, or agent retrieval memory.
Read `references/documentation-routing.md` before writing docs. Read
`references/evaluation-harness.md` before finalizing output.

## Use When

Use this skill when Codex must implement, refactor, certify, or roll out Meridian work with
explicit validation evidence, docs synchronization, and residual-risk reporting.

Trigger examples:

- "Implement this plan and prove it with the required gates."
- "Certify this provider change is complete."
- "Update the AI skill catalog and verify drift checks."
- "Improve this Codex agent from judge feedback and update the baseline."

## Do Not Use When

Use `meridian-blueprint` when the user only wants a design, `meridian-code-review` when the user
only wants findings, and `meridian-brainstorm` when the user is still exploring options.

Non-trigger examples:

- "Write a technical blueprint only."
- "Review this diff for bugs."
- "Brainstorm product bets for next quarter."

## Definition of Done

A task delivered by this skill is complete when **all** of the following are true:

- [ ] **Build passes:** at least one of `dotnet build` or `dotnet test` targeting the touched project runs without errors.
- [ ] **Tests cover the change:** tests for happy path, failure path, and cancellation/disposal exist or are cited as a gap.
- [ ] **Validation evidence is explicit:** the final response includes exact commands and their pass/fail results.
- [ ] **Documentation is in sync:** existing docs covering the changed behavior are updated in-place, or a new doc is created in the correct subtree with a cross-link from the nearest index.
- [ ] **AI tooling gates pass for AI/tooling changes:** run `make ai-verify`, run `make ai-arch-check`, and confirm `.github/workflows/ci.yml` still contains the `Validate AI contract drift` step.
- [ ] **Agent improvements are promoted through the eval loop:** baseline, feedback source, candidate diff, aggregate score, threshold, retry count, and updated catalog paths are recorded.
- [ ] **Rubric score >= 8/10, no category at 0:** `scripts/score_eval.py` is run and the report is included in the response.
- [ ] **Performance-sensitive paths are annotated:** any hot path touched by the change includes an explicit note on allocation, async, or buffering risk.
- [ ] **Summary is traceable:** the closing summary links requirement -> files changed -> validation artifact -> doc update.

## Workflow

1. Define requested behavior, risks, and acceptance checks.
2. Identify impacted layers and likely performance-sensitive paths before editing.
3. Implement the smallest safe change set that satisfies the request.
4. Run targeted validation and capture exact command results.
5. Update related documentation; if missing, add docs in the correct doc area.
6. Run the evaluation harness and report pass/fail with evidence.
7. Summarize code and docs updates and call out residual risk.

## Self-Improving Agent Loop

Use `docs/ai/codex/self-improving-agents.md` whenever this skill improves an agent, prompt, skill,
eval rubric, or graph-backed memory workflow. Treat the current artifact as the baseline, capture
human feedback or LLM-as-judge findings, apply one candidate change at a time, run the relevant
evals, compare the aggregate score to the target threshold, and promote only after validation,
catalog updates, and rollback notes are complete.

For graph, semantic-memory, or retrieval-agent changes, require source-backed temporal records,
non-destructive versioning, retention and pruning policy, staged concurrency with backpressure,
token-cost controls, and source-cited retrieval behavior before claiming the agent is production
ready.

## Handoffs

- Receive selected plans from `meridian-blueprint`, actionable fixes from `meridian-code-review`, and test gaps from `meridian-test-writer`.
- Hand provider-specific implementation details to `meridian-provider-builder` before final assurance when the provider contract itself is still being built.
- Hand archive classification to `meridian-archive-organizer` before certifying archive or structure changes.

## Validation

- Run the narrowest command that proves the touched surface, then broaden only when risk justifies it.
- For AI/tooling changes, run `python3 build/scripts/docs/check-codex-skills.py --summary`, `python3 build/scripts/docs/check-ai-inventory.py --summary`, `python3 build/scripts/docs/validate-skill-packages.py`, `python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --summary`, and `git diff --check`.
- For registered source module changes, run `python3 build/scripts/docs/mark-stale-docs.py --write --summary` before source-doc updates so only stale module docs are targeted, then run `python3 build/scripts/docs/validate-doc-hashes.py --summary` after source README and registry review. Refresh the hash manifest with `--write` only when code/docs alignment is intentionally accepted.
- Confirm `.github/workflows/ci.yml` still contains the `Validate AI contract drift` step when AI workflow behavior changes.

## Execution Discipline

Use these gates before and during implementation.

### Concurrent Implementation Gate

- Check `git status --short` before editing and treat unrelated changes as user-owned.
- Split concurrent work only across disjoint files, projects, or subsystems with explicit ownership.
- Keep one coordinator responsible for integrating worker results, resolving conflicts, and running final validation.
- Do not assign overlapping write scopes unless the overlap is intentionally sequenced and documented.
- Avoid broad shared-output builds during parallel work. For concurrent .NET builds, use isolated output through `python3 build/python/cli/buildctl.py build --isolation-key codex-<task>`.

### Narrow Validation Gate

- Run the smallest command that covers the touched layer before broadening.
- Prefer filtered `dotnet test`, package-local dashboard commands, doc-only checks, and `--no-build` after a valid isolated build.
- Use `npm --prefix src/Meridian.Ui/dashboard ...` for browser-workstation validation and avoid WPF checks unless WPF or shared desktop contracts changed.
- Treat broad `make test`, full solution builds, full Vitest runs, and release packaging checks as escalation paths, not the default proof for narrow edits.

### Cosmetic Churn Gate

- Skip capitalization, uncapitalization, whitespace, punctuation, and wording-only edits unless they fix canonical naming, broken docs, accessibility text, lint/test failures, API contract names, or user-visible copy correctness.
- Do not run broad formatters to normalize files outside the task scope.
- If a tempting cosmetic cleanup is intentionally skipped, mention it only when it clarifies scope control.

### Naming and Boundary Preflight

- Use PascalCase C# file names that match the primary type.
- Prefix interface files with `I`; name tests `{SourceName}Tests.cs`; name endpoint files `{Feature}Endpoints.cs`.
- Keep shared DTOs in `Contracts`, provider abstractions in `ProviderSdk`, configuration in `Core/Config`, and UI-specific contracts in `Ui.Services/Contracts`.
- Preserve project boundaries: `Contracts` must not reference other projects, `Ui.Services` must not reference `Application`, and lower layers must not depend on higher orchestration layers.

## Requirement Type Detection

Use this decision tree before starting any task to pick the right validation lane:

```text
What are you assuring?
|-- Feature completeness vs. blueprint/acceptance criteria
|   -> Lane: requirement matrix + targeted unit/integration tests
|-- Scope alignment to an issue or roadmap item
|   -> Lane: requirement matrix + file mapping + acceptance criteria check
|-- Documentation sync after a code change
|   -> Lane: doc routing matrix + cross-reference validation
|-- Capability discovery / AI catalog update
|   -> Lane: agent/skill symmetry check (docs/ai/agents/ + docs/ai/skills/)
`-- Rollout readiness
    -> Lane: build gate + test gate + deployment gates (all CRITICAL)
```

Each lane produces different required artifacts. Match the lane to the task before collecting
evidence.

## Skill/Agent Authoring Lane

Use this lane whenever the task creates or updates a Codex, Claude, or GitHub AI package.

- Use `$skill-creator` when it is available, especially for `agents/openai.yaml` regeneration and quick package validation.
- Inspect only the relevant Meridian project instincts when local learned behavior would help. Treat each instinct as a hint to verify against the current repo state before turning it into instructions.
- Keep the main skill file concise and imperative. Move detailed material into `references/`, deterministic helpers into `scripts/`, and output resources into `assets/`.
- Preserve host-specific metadata rules. For repo-local Codex skills, keep frontmatter to `name` and `description`. For portable Claude packages, preserve the metadata required by that host.
- Keep mirrored Codex, Claude, and GitHub agent guidance aligned only when a shared workflow or policy changes. If the user scopes the work to Codex only, update the Codex skill, Codex catalog, and `agents/openai.yaml` metadata without widening into Claude or GitHub surfaces.
- Avoid auxiliary docs inside skill folders unless they directly support execution or are required by the host format.
- If `agents/openai.yaml` exists, regenerate or update it so the UI-facing metadata still matches the skill instructions.
- Run `python build/scripts/docs/check-ai-inventory.py --summary` after Codex skill metadata or shared-context edits, and run representative tests for any added or changed scripts.

## Correctness Guardrails

- Preserve existing contracts, nullability expectations, and cancellation flow.
- Keep layer boundaries explicit across UI, service, storage, provider, execution, and governance seams.
- For browser workstation work, keep `src/Meridian.Ui/dashboard/` as the default UI lane, preserve `/workstation/` route behavior, and keep MVVM-owned labels, disabled reasons, empty states, and live-region status out of leaf React components.
- Do not create mobile apps, mobile-specific surfaces, or mobile-first workflow guidance unless the user or roadmap explicitly reopens mobile development.
- Add or extend tests for happy path, failure path, and cancellation/disposal where relevant.
- Prefer deterministic behavior over timing-sensitive heuristics.

## Performance Guardrails

- Inspect hot paths for avoidable allocations, synchronous blocking, and unbounded buffering.
- Avoid `.Result` and `.Wait()` on async flows.
- Keep logging and serialization costs proportional to execution frequency.
- When introducing loops or streams, define cancellation and backpressure behavior.

## Documentation Synchronization Rules

- Update docs in the same change when behavior, interfaces, architecture, or operations change.
- Prefer editing an existing doc when one already covers the topic.
- Create new docs only when no suitable home exists.
- For new docs, choose placement using `references/documentation-routing.md` and add cross-links from the nearest index or README.
- When runtime config or persistence semantics change, update the AI-facing docs and shared context in the same change: the relevant `docs/ai/*` pages, `../_shared/project-context.md`, and any mirrored Codex, Claude, or GitHub agent files that teach the affected workflow.
- Keep documentation concrete: what changed, why, and how to use or operate it.
- For source READMEs, add optional sections only when they add value: plans, end-user value, benchmarks/performance, operational evidence, security or credential handling, API/contract notes, and migration/archive notes.
- Treat `docs/source/generated/stale-docs.json` as the source-doc work queue and `docs/source/generated/source-hash-manifest.json` as the accepted code/docs alignment checkpoint. If source code changed but documentation did not, mark the stale module first, update only those docs when possible, and leave the hash gate failing until documentation is reviewed or updated.

## AI Tooling Gates

For AI tooling, Codex skill, Codex catalog, prompt, docs automation, or assistant-workflow changes, classify validation as required, advisory, or maintenance/reporting.

- Required quality gates: `make ai-verify`, `make ai-arch-check`, and the CI `Validate AI contract drift` step in `.github/workflows/ci.yml`.
- Advisory tooling: `make ai-audit*`, `make ai-report`, `make ai-docs-freshness`, `make ai-docs-drift`, `make ai-docs-sync-report`, `make ai-arch-check-summary`, and `make ai-arch-check-json`.
- Maintenance/reporting tooling: `make ai-maintenance-light`, `make ai-maintenance-full`, `make ai-docs-archive`, and `make ai-docs-archive-execute`.
- Keep the root `make help` grouping aligned with this split so contributors can tell blocking gates from optional tooling.

## Automation Scripts

Use bundled scripts to keep execution fast and consistent:

- `scripts/doc_route.py` — recommend documentation location, filename, and whether cross-linking is required.
- `scripts/score_eval.py` — compute rubric totals and generate a standardized eval report.
- `scripts/run_evals.py` — run the deterministic eval harness against `evals/evals.json` cases.

## Evaluation Requirement

Treat `references/evaluation-harness.md` as mandatory for this skill. Always return:

- which scenario was evaluated
- rubric scores by category
- failing checks and corrective follow-ups
- exact command evidence for tests and build checks

## Output Checklist

Before finishing, confirm:

- [ ] code compiles or tests pass for the touched surface
- [ ] performance-sensitive changes were reviewed with explicit notes
- [ ] docs were updated, or newly added in the correct location
- [ ] stale source docs were marked with `mark-stale-docs.py` and source/docs hash alignment was checked with `validate-doc-hashes.py` when registered `src/**` files changed
- [ ] AI/tooling changes ran `make ai-verify`, `make ai-arch-check`, and confirmed the CI contract-drift step
- [ ] final response includes a Code + Docs Sync Matrix with code change, doc owner, doc update status, and validation result
- [ ] evaluation harness was completed with a rubric score summary (>= 8/10, no category at 0)
- [ ] summary includes validation commands and any residual risk

## Output Standards

Use this skill's final response structure unless the user requested a narrower report. Include
implemented behavior, docs sync, required gates, narrow checks, advisory checks, concurrency/scope,
and residual risk.

Use this response structure for implementation turns:

```md
**Implemented**
- Code: <behavior-level changes>
- Docs: <updated docs or "No docs needed: <reason>">

**Validation**
- Required gates: <commands/results>
- Narrow checks: <commands/results>
- Advisory checks: <commands/results or "not run: <reason>">

**Concurrency And Scope**
- Work split: <none | worker ownership>
- Isolation: <build/test isolation used>
- Skipped churn: <cosmetic edits intentionally avoided>

**Residual Risk**
- <real remaining blocker or gap>
```
