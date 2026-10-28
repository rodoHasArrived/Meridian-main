---
name: meridian-implementation-assurance
description: Implement or certify Meridian changes against acceptance criteria with risk-based validation, docs sync, and explicit evidence gaps. Use for feature completion, refactors, rollout readiness, or Codex skill and agent improvements; distinguish implemented behavior, verified outcomes, and blocked proof.
---

# Meridian Implementation Assurance

Make completion claims only when the requested behavior, integration paths, docs, and observed
validation results agree. Keep the skill compact; load detailed references only when the current
task needs them.

> **GitHub Copilot equivalent:** [`.github/agents/implementation-assurance-agent.md`](../../../.github/agents/implementation-assurance-agent.md)
> **Claude Code equivalent:** [`.claude/skills/meridian-implementation-assurance/SKILL.md`](../../../.claude/skills/meridian-implementation-assurance/SKILL.md)
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)

## Load Order

1. Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before
   implementation or certification work.
2. Read `references/documentation-routing.md` only before adding a new doc or when doc placement is
   unclear.
3. Read `references/evaluation-harness.md` before scoring this skill, updating eval fixtures, or
   reporting rubric results.
4. Read [`docs/ai/codex/self-improving-agents.md`](../../../docs/ai/codex/self-improving-agents.md)
   only when changing prompts, Codex skills, agent profiles, eval rubrics, or agent memory/retrieval
   behavior.

## Use When

Use this skill when Codex must implement, refactor, certify, or roll out Meridian work and provide
evidence that the change is correct, documented, and operationally safe.

Trigger examples:

- "Implement this plan and prove it with the required gates."
- "Certify this provider change is complete."
- "Validate this rollout and update docs."
- "Improve this Codex skill from eval feedback and update the baseline."

## Do Not Use When

- Use `meridian-blueprint` for a design-only or plan-only request.
- Use `meridian-code-review` for review-only findings.
- Use `meridian-brainstorm` for open-ended ideation.
- Use `meridian-test-writer` when the only deliverable is test authoring.

Non-trigger examples:

- "Write a technical blueprint only."
- "Review this diff for bugs."
- "Brainstorm product bets for next quarter."
- "Write unit tests for this existing service."

## Handoffs

- Receive selected plans from `meridian-blueprint`, actionable findings from `meridian-code-review`,
  and test gaps from `meridian-test-writer`.
- Hand provider-specific contract work to `meridian-provider-builder` before final assurance when
  the provider adapter or manifest is still being built.
- Hand documentation-only edits to `meridian-docs` and archive classification to
  `meridian-archive-organizer` when those are the main deliverables.

## Workflow

1. Translate the request into observable acceptance criteria, affected boundaries, risks, and a
   validation lane. Resolve routine choices from current evidence; ask only for missing decisions
   that materially change the result.
2. Check `git status --short`; preserve unrelated dirty work as user-owned.
3. Inspect the impacted source, nearest README, source registry, and canonical docs before editing.
4. Implement the smallest complete change across the relevant boundaries. Check registration,
   configuration, callers, persistence, and shared service/read-model seams when affected; a new
   class or passing isolated unit test does not prove the workflow is connected.
5. Run the narrowest meaningful proof first. Cover the intended outcome and material failure paths;
   broaden when risk or repository policy requires it. Reuse evidence only while its inputs remain
   unchanged, and rerun affected checks after fixes.
6. Update existing docs in place, or create a new doc through the routing guide and link the nearest
   index.
7. Reconcile every acceptance criterion with implementation and evidence before reporting status.
   Use a compact matrix for multi-part work; a short paragraph suffices for a small change.

## Evidence And Completion

Track each material criterion as `verified`, `implemented-unverified`, `blocked`, or `deferred`.
Tie it to changed paths and the command, observed result, or inspected artifact that supports it.
Do not silently defer required work; finish all work that remains possible within the task.

- Distinguish commands actually run from proposed commands, successful checks from failed attempts,
  and real integrations from mocks, fixtures, or static inspection.
- Record the target, result, and material limits of each check. A build proves compilation; a unit
  test proves the covered case; neither alone establishes persistence, browser behavior, rollout
  readiness, or external service compatibility.
- Compare failures with a baseline when feasible. Label failures as pre-existing only with evidence;
  otherwise report their origin as unresolved. Fix regressions introduced by the change.
- If a tool, service, platform, or credential is unavailable, complete independent work, state the
  unverified criterion and blocker, and identify the next proof command. Never turn a blocked check
  into a pass or bypass a required gate.
- Reserve “complete,” “certified,” and “ready to roll out” for the scope actually verified and its
  required gates. Report local implementation, local validation, publication, and CI separately.
  Follow repository publication policy and the user's authorized scope.

For financial behavior, check the invariants the change can affect: decimal precision and rounding,
signs, units and currencies, effective dates and time zones, idempotency, reconciliation, durable
writes, and audit provenance. Exercise the relevant workflow boundary and failure path rather than
adding a blanket checklist to unrelated work.

## Validation

- Prefer direct Windows-friendly commands: `python`, `dotnet`, `npm --prefix`, and `pwsh`.
- Use GNU Make wrappers only when `where.exe make` finds a usable `make`; `make ai-verify` and
  `make ai-arch-check` are wrappers for the underlying Python/script AI gates, not the only valid
  proof path.
- For AI tooling, Codex skills, catalog, prompt, docs automation, or assistant-workflow changes, run
  or account for:

```bash
python build/scripts/docs/check-codex-skills.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/validate-skill-packages.py
python .codex/skills/meridian-implementation-assurance/scripts/skill_script_advisor.py audit --skill meridian-implementation-assurance --summary
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --summary
git diff --check
```

- Confirm `.github/workflows/meridian-ci.yml` invokes the canonical `scripts/ci.sh` docs lane,
  which must retain the `Validate AI contract drift` check when AI workflow behavior changes.
- For registered `src/**` module changes, mark stale docs before source-doc updates, then validate
  hashes after reviewing the nearest source README and registry entry.
- For browser workstation work, use `npm --prefix src/Meridian.Ui/dashboard ...` and exercise the
  rendered affected workflow, keyboard behavior, console, and network results when available.
  Report simulated backend behavior and unavailable browser proof. Avoid WPF checks unless WPF or
  shared desktop contracts changed.
- For WPF or .NET work, build first when stale `--no-build` outputs are likely, then run the
  smallest filtered test.

## Assurance Gates

- **Scope gate:** Keep one coordinator responsible for integration. Split parallel work only across
  disjoint files or subsystems; use `docs/ai/working-memory.md` for concurrent lane claims, facts,
  assumptions, merge order, and validation reuse.
- **Correctness gate:** Preserve contracts, nullability, cancellation flow, storage durability, and
  project boundaries. Do not create mobile-specific apps or workflows.
- **Performance gate:** Inspect hot paths for allocation, sync-over-async, unbounded buffering,
  logging cost, serialization cost, cancellation, and backpressure.
- **Docs gate:** Update docs when behavior, workflow, contract, configuration, AI guidance, or
  operator-facing behavior changes. Prefer existing docs over new docs.
- **Cosmetic gate:** Skip capitalization, whitespace, punctuation, or wording-only churn unless it
  fixes naming, broken docs, accessibility, lint/test failures, contract names, or user-visible copy.

## Requirement Lanes

Choose the lane before collecting evidence:

```text
Feature completeness vs. blueprint/acceptance criteria
  -> requirement matrix + targeted unit/integration tests
Scope alignment to issue or roadmap item
  -> requirement matrix + file mapping + acceptance check
Documentation sync after a code change
  -> doc routing matrix + cross-reference validation
Capability discovery / AI catalog update
  -> Codex skill metadata + docs/ai inventory checks
Rollout readiness
  -> build gate + test gate + deployment or operational gates
Prompt, skill, agent, or rubric improvement
  -> baseline + feedback + candidate diff + eval score + promotion notes
```

## Skill And Agent Authoring

Use this lane for Codex, Claude, GitHub, or portable Agent Skills packages.

- Use `$skill-creator` when available.
- Keep repo-local Codex `SKILL.md` frontmatter to `name` and `description`.
- Keep the main skill file concise and imperative. Move detailed reference material to
  `references/`, deterministic helpers to `scripts/`, and reusable output resources to `assets/`.
- Before adding or optimizing a bundled helper, run
  `scripts/skill_script_advisor.py audit --skill <skill> --summary`; use
  `scripts/skill_script_advisor.py scaffold --skill <skill> --name <script-name> --purpose "<purpose>"`
  only for repeated, fragile, or validation-critical work, then replace the scaffold body and run a
  representative script command.
- Keep `agents/openai.yaml` synchronized with the skill text. Do not add icons, brand colors,
  policy blocks, or MCP dependencies unless they are real requirements.
- For Codex-only changes, update the Codex skill, Codex catalog, and OpenAI metadata only; do not
  widen into `.claude/skills/`, `.agents/skills/`, or GitHub agent surfaces unless the workflow
  itself is shared.
- Promote prompt, skill, profile, rubric, graph-memory, or retrieval changes only through the
  self-improving-agent loop: baseline, feedback, candidate diff, eval score, threshold, retry count,
  catalog updates, and rollback notes. Dry-run fixture checks and rubric arithmetic are not agent
  behavior scores. Preserve historical live baselines until a comparable live run and assertion
  review support promotion; otherwise report the candidate as behaviorally unmeasured.

## Automation Scripts

- `scripts/doc_route.py` recommends documentation location, filename, and cross-link requirements.
- `scripts/run_evals.py` validates fixture structure in dry-run mode. Live `codex exec` runs require
  explicit opt-in and an isolated worktree or scratch clone; automated trace checks are heuristics,
  and each scenario assertion still needs evidence review.
- `scripts/score_eval.py` validates integer scores and category evidence, computes rubric totals,
  and rejects passing verdicts with failed checks. Exit codes: 0=pass, 1=failed rubric, 2=invalid input.
- `scripts/skill_script_advisor.py` audits bundled script resources, identifies optimization
  findings, and scaffolds repo-safe Python helpers for self-improving skill work.

## Output Standards

Use this final response shape unless the user asks for a narrower report:

```md
**Implemented**
- Status: <verified | implemented-unverified | blocked; identify affected scope>
- Code: <behavior-level changes linked to acceptance criteria>
- Docs: <updated docs or "No docs needed: <reason>">

**Validation**
- Required gates: <commands actually run, results, and blocked or skipped checks with reasons>
- Narrow checks: <commands/results>
- Advisory checks: <commands/results or "not run: <reason>">

**Concurrency And Scope**
- Work split: <none | worker ownership>
- Isolation: <build/test isolation used>
- Skipped churn: <cosmetic edits intentionally avoided>

**Residual Risk**
- <unverified criteria, concrete impact, and next proof step; or none>
```
