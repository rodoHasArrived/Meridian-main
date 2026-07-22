# Copilot Repository Instructions

**Last Updated:** 2026-06-16

Use these instructions for every task in this repository to improve quality, reliability, and review speed.

> **See also:** [docs/ai/assistant-workflow-contract.md](../docs/ai/assistant-workflow-contract.md) for shared provider-agnostic rules, [CLAUDE.md](../CLAUDE.md) for full project context, and [docs/ai/README.md](../docs/ai/README.md) for the master AI resource index.

## 0) Standard execution flow

For each task, follow this sequence:

1. Restate the requested change in one sentence.
2. Identify acceptance criteria before coding.
3. Make the smallest possible set of edits.
4. Run targeted validation commands.
5. For multi-agent or multi-lane tasks, use `docs/ai/agent-handoff-checklist.md` when handing off between phases.
6. Select a mode from `docs/ai/work-modes.md` before implementation; escalate only when risk or scope increases.
7. For concurrent lanes, initialize `docs/ai/parallel-task-manifest-template.md` before edits to keep ownership boundaries explicit.
8. If the task needs AI validators, routing tools, or maintenance scripts, load `docs/ai/tooling/README.md` instead of rediscovering command lanes ad hoc.
9. For broad generation, domain modeling, workflow design, or architecture-sensitive refactors, load MDIF: `docs/architecture/meridian-development-intelligence-framework.md`, `docs/architecture/meridian-vision.md`, `docs/architecture/meridian-domain-model.md`, `docs/domain/README.md`, and `docs/ai/context/README.md`.
10. Summarize what changed, why, and how it was validated.
11. Local work may happen on `main` when the user explicitly requests it or the checkout is
    intentionally operating there. Do not bypass GitHub branch protections; for PR-ready publishing,
    use a `codex/<short-task-name>` branch and a PR targeting `main`.

**No mobile development lane:** do not create mobile applications, mobile-specific product
surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
mobile-first workflows. Responsive browser validation may continue for the browser workstation.

Keep product work grounded in current source evidence, the roadmap registry, and the design charter.
Treat prior baselines and named productization targets as roadmap/status evidence, not development
ceilings; expansion lanes can proceed when current source, roadmap, or user direction supports them.

If the task request is ambiguous, document assumptions in the PR body.

## 1) Prefer well-scoped tasks

When working from an issue or prompt, treat it as an implementation contract:

- Restate the exact problem being solved.
- Confirm acceptance criteria before coding.
- Limit changes to the smallest file set that satisfies the task.
- Call out assumptions when requirements are ambiguous.

If requirements are unclear, propose concrete acceptance criteria and proceed with the safest interpretation.

## 2) Choose tasks appropriate for an agent

Good fits:

- Bug fixes with reproducible symptoms.
- Targeted UI adjustments.
- Test coverage improvements.
- Documentation updates.
- Refactors with clear boundaries.

Escalate or avoid autonomous changes for:

- Security-sensitive or auth-critical logic.
- Broad architectural redesigns.
- High-risk production incident work.
- Ambiguous tasks without verifiable outcomes.

## 3) Quality bar for every change

Always do the following before opening a PR:

1. Read `docs/ai/ai-known-errors.md` and apply relevant prevention checks.
2. Restore/build with Windows targeting enabled on non-Windows systems.
3. Run tests relevant to touched code.
4. Update docs when behavior, interfaces, or workflows change.
5. Keep PR title/body in sync with final implemented behavior.
6. Keep provider-specific guidance aligned with `docs/ai/assistant-workflow-contract.md`.
7. When editing `src/**`, read the nearest source README and keep `docs/source/data/*.yml`
   synchronized when module ownership, validation, roadmap mapping, diagrams, or TODO scope changes.
8. If editing shared handoff, manifest, or AI work-mode guidance, run `python build/scripts/docs/check-ai-handoff.py --strict`.
9. For completed PR-ready work, run `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate` is the authoritative merge check.

## 4) Build and test commands

Use the fastest command set that validates your change. For non-test-only edits, run restore + build first:

```bash
bash scripts/ci.sh
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
```

Common targeted test commands:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true
```

If only one area is affected, run the nearest test project first and expand scope only if needed.
When local machine limits, dependency restore, or MSBuild locks make validation unreliable, push the
branch and run the manual GitHub-hosted `Targeted Test` workflow instead of repeatedly retrying
broad local scripts. Select a whitelisted `mode`; the .NET lane uses `mode=dotnet-filtered` with a
repo-relative test project under `tests/` and a non-empty `dotnet_filter`:
After timed-out generation, build, or test attempts, first run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout.

```bash
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
```

## 5) Response quality expectations

- Explain *what* changed and *why*.
- Mention risks, tradeoffs, and follow-up items.
- Include exact validation commands and outcomes.
- Keep edits consistent with existing architecture and naming.

When a change is documentation-only, explicitly state that no runtime behavior was modified.

## 6) Path-specific instructions

Also follow any matching files under `.github/instructions/**/*.instructions.md` for language-, path-, and test-specific guidance.

Relevant examples:

- `.github/instructions/csharp.instructions.md` for C# source files.
- `.github/instructions/wpf.instructions.md` for WPF/MVVM files.
- `.github/instructions/dotnet-tests.instructions.md` for C# test changes.
- `.github/instructions/docs.instructions.md` for Markdown edits.
- `.github/instructions/source-documentation.instructions.md` for source README and registry sync.

## 7) Related resources

- [`CLAUDE.md`](../CLAUDE.md) — comprehensive project context, architecture, conventions
- [`docs/ai/README.md`](../docs/ai/README.md) — master AI resource index with reading order by task type
- [`docs/architecture/meridian-development-intelligence-framework.md`](../docs/architecture/meridian-development-intelligence-framework.md) — MDIF context spine for broad generation and architecture-sensitive work
- [`docs/ai/ai-known-errors.md`](../docs/ai/ai-known-errors.md) — mandatory error prevention registry
- [`docs/ai/copilot/instructions.md`](../docs/ai/copilot/instructions.md) — compact Copilot host guide and routing links
- [`docs/ai/tooling/README.md`](../docs/ai/tooling/README.md) — shared AI validator and script index
- [`agents/code-review-agent.md`](agents/code-review-agent.md) — 7-lens code review framework
- [`prompts/`](prompts/) — 16 reusable prompt templates
