---
name: meridian-archive-organizer
description: Archive deprecated, superseded, historical, or misplaced Meridian code and documentation while keeping the repository structure organized. Use when Codex needs to decide whether a file should stay active or move into `archive/`, relocate outdated code or docs into the correct archive bucket, clean up folder sprawl, answer "where should this live?" for Meridian repository-structure work, or perform an evidence-backed archive/reference-trace pass with repeatable validation.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files, archive
  placement references, and optional trace scripts when the host permits filesystem access.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Archive Organizer

Use this skill when the task is to retire stale material without losing useful history or making Meridian harder to navigate.

Read these in order:
1. `../_shared/project-context.md`
2. `../../../archive/docs/README.md`
3. `references/archive-placement-guide.md`
4. `references/evaluation-harness.md` before finalizing a broad archive sweep or changing this skill

## Definition of Done

A task handled with this skill is complete only when all of the following are true:

- Each target has an explicit classification: `active`, `archive-doc`, `archive-code`, `delete`, or `already-archived`.
- Strong references and weak references are separated, either by direct reasoning or by `scripts/trace_archive_candidates.py`.
- The destination matches the archive bucket or active folder rules in `references/archive-placement-guide.md`.
- Any moved documentation has its README, index, or nearby cross-link updated.
- A narrow validation command or trace artifact is cited in the final response.
- For ambiguous, multi-file, or skill-evolution work, `references/evaluation-harness.md` is used and the eval result is reported.

## Classification Lanes

Choose a lane before moving anything:

### `dated-doc-snapshot`

Use for date-stamped status, audit, roadmap, or one-off narrative documents.

- Archive when strong references are gone and only weak references remain.
- Prefer `archive/docs/summaries/` unless the document is clearly an assessment, plan, or migration note.

### `automation-owned-surface`

Use for generated or machine-readable documentation artifacts that look stale but are still owned by repo tooling.

- Keep active when docs/build/workflow tooling explicitly writes or consumes the file.
- Typical examples: `docs/status/*.json`, `docs/generated/*`, and current docs automation outputs.

### `local-scratch`

Use for hidden folders, logs, ad hoc captures, and local tool output.

- Prefer `delete` or `.gitignore`, not archive.
- Treat `.playwright-cli/`, transient logs, and scratch captures as this lane unless the user explicitly wants historical retention.

### `retired-code`

Use for code or tests that may need historical retention.

- Require stronger evidence than docs lanes: build/test safety, project inclusion awareness, and caller/link/doc checks.
- Prefer mirroring the original relative path beneath `archive/code/`.

## Workflow

1. Pick the classification lane that matches the target.
2. Gather evidence before moving anything: active references, project or solution inclusion, docs links, test usage, and whether the item still reflects current guidance.
3. Use `scripts/trace_archive_candidates.py` for docs, dated snapshots, JSON sidecars, or root-structure ambiguity.
3. Apply the smallest structure-preserving move:
   - keep current material in canonical active folders
   - move historical docs into the right `archive/docs/<bucket>/` folder
   - move retired code into `archive/code/` and preserve the original relative path when practical
4. Update indexes, links, or nearby docs when discoverability changes.
5. Run the narrowest validation that proves the move did not break build, test, or documentation navigation.
6. For broad or ambiguous sweeps, run the evaluation harness.
7. Report what moved, why it moved, what stayed active, and any follow-up cleanup still worth doing.

## Decision Rules

- Keep files active when they are executable, referenced, or still define current guidance.
- Keep generated documentation outputs active when the automation/docs tooling explicitly writes them there and surrounding docs treat them as current machine-readable surfaces.
- Treat references from active README files, planning indexes, and human-maintained guidance as strong evidence that a document should stay active.
- Downgrade README or sibling-doc references when they explicitly frame the target as a historical snapshot, prior review context, or archived background rather than current guidance.
- Treat references from generated tree listings, stale validation reports, and inventory-style snapshots as weak evidence; they do not by themselves keep a dated document active.
- Treat references coming only from already archived material as weak historical evidence, not proof that the active copy must stay where it is.
- Ignore `.codex/`, `.claude/`, and similar AI/worktree mirror content as archive evidence unless the user explicitly asks to audit those surfaces too.
- Archive documentation instead of deleting it when it has historical, audit, migration, or planning value.
- Archive code instead of deleting it when it is no longer part of the active build or test graph but may still matter for migration context, rollback archaeology, or reference.
- Delete only low-value artifacts with no reference value, such as transient outputs, duplicates, or generated clutter that should not live in git.
- When evidence is incomplete, prefer archiving over deletion and say what remains uncertain.

## Placement Rules

- Use `archive/docs/assessments/` for audits, evaluations, and analysis reports.
- Use `archive/docs/plans/` for completed, superseded, or abandoned plans and cleanup roadmaps.
- Use `archive/docs/summaries/` for snapshots, one-off reports, and historical status notes.
- Use `archive/docs/migrations/` for legacy migration or platform-transition material.
- Use `archive/docs/assets/` for diagrams and supporting media tied to archived docs.
- Use `archive/code/` for retired source or tests, and mirror the previous path beneath `archive/code/` when practical.
- Keep active code under `src/`, active tests under `tests/`, current docs under `docs/`, automation and support scripts under `scripts/`, deployment material under `deploy/`, and generated outputs under `artifacts/` or ignored build folders.
- Keep automation-owned status artifacts in `docs/status/` when build/docs tooling and documentation references expect them there.
- Treat local hidden tool scratch folders such as `.playwright-cli/` as `delete` or `.gitignore` candidates, not archive candidates.

## Guardrails

- Do not archive code that is still included by a project, solution, source generator, XAML binding, reflection path, or current docs link without evidence.
- Do not leave broken relative links after moving docs.
- Do not mix structural archiving with feature implementation unless the user asks for both.
- Keep each pass theme-based: stale docs, retired prototype, legacy tests, or folder cleanup.
- Preserve filenames unless a rename materially improves placement clarity.
- Update the archive indexes or placement guide when a move introduces a new recurring pattern.

## Validation Guidance

- For docs-only moves, update links and search for stale references in docs, config, and README-style entrypoints.
- For code moves, confirm the files are no longer referenced by `*.csproj`, `Meridian.sln`, `Directory.Build.props`, tests, or active docs before moving them.
- For generated docs or JSON outputs, check `build/scripts/docs/`, workflow files, and documentation-automation docs before reclassifying them as stale.
- For root-structure cleanup, confirm the final top-level layout still matches `references/archive-placement-guide.md`.
- Prefer targeted checks such as `Select-String`, narrow `dotnet build` or `dotnet test` commands, and archive index updates over full-repo validation unless the move is broad.

## Automation Scripts

Use bundled scripts to keep archive work deterministic:

- `scripts/trace_archive_candidates.py`
  - Trace references, separate strong vs weak evidence, and suggest a classification.
  - Example: `python scripts/trace_archive_candidates.py --path docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md --path docs/status/docs-automation-summary.json`
- `scripts/run_evals.py`
  - Run deterministic repo-grounded eval cases from `evals/evals.json`.
  - Example: `python scripts/run_evals.py --all`
- `scripts/score_eval.py`
  - Produce a rubric-style score block for a manual archive pass or skill revision.
  - Example: `python scripts/score_eval.py --scenario A --scores "{\"classification_accuracy\":2,\"reference_trace_quality\":2,\"placement_correctness\":2,\"safety_guardrails\":1,\"cleanup_follow_through\":2}"`

## Evaluation Requirement

Treat `references/evaluation-harness.md` as mandatory when:

- changing this skill's heuristics
- running a multi-file archive sweep
- archiving dated documents that still have mixed evidence
- claiming the skill is more robust after an iteration

Return:

- which lane or scenario was evaluated
- the command evidence used
- the recommendation or move outcome
- the rubric score or deterministic eval results

## Output Standards

- State the classification for each moved item.
- Name the old path and new path.
- Summarize the evidence that made the move safe.
- Mention any links, indexes, or structure docs updated.
- Separate follow-up candidates from the work completed in the current pass.

## Output Checklist

Before finishing, confirm:

- [ ] Every target has a classification and reason.
- [ ] Strong vs weak references were separated.
- [ ] Any archive destination matches the bucket rules.
- [ ] README/index links were updated when paths changed.
- [ ] Validation or trace commands are cited explicitly.
- [ ] Evaluation results are included when the sweep or skill update was non-trivial.
