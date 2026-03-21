# Copilot Repository Instructions

Use these instructions for every task in this repository to improve quality, reliability, and review speed.

> **See also:** [CLAUDE.md](../CLAUDE.md) for full project context | [docs/ai/README.md](../docs/ai/README.md) for the master AI resource index

## 0) Standard execution flow

For each task, follow this sequence:

1. Restate the requested change in one sentence.
2. Identify acceptance criteria before coding.
3. Make the smallest possible set of edits.
4. Run targeted validation commands.
5. Summarize what changed, why, and how it was validated.

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

## 4) Build and test commands

Use the fastest command set that validates your change. For non-test-only edits, run restore + build first:

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
```

Common targeted test commands:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true
```

If only one area is affected, run the nearest test project first and expand scope only if needed.

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

## 7) Related resources

- [`CLAUDE.md`](../CLAUDE.md) — comprehensive project context, architecture, conventions
- [`docs/ai/README.md`](../docs/ai/README.md) — master AI resource index with reading order by task type
- [`docs/ai/ai-known-errors.md`](../docs/ai/ai-known-errors.md) — mandatory error prevention registry
- [`docs/ai/copilot/instructions.md`](../docs/ai/copilot/instructions.md) — extended Copilot guide with project structure and decision tree
- [`agents/code-review-agent.md`](agents/code-review-agent.md) — 6-lens code review framework
- [`prompts/`](prompts/) — 16 reusable prompt templates
