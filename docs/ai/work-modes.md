# AI Work Modes

Use these modes to right-size context and validation effort without skipping safety or correctness.
Pair mode selection with the active entry in `docs/ai/model-routing-policy.json` when a task requires explicit model-class routing.

## Mode Selection

| Mode | Use when | Context scope | Validation expectation |
| --- | --- | --- | --- |
| Lightweight | Small docs edits, narrow prompt tweaks, localized AI guidance touch-ups | Read only directly affected files plus shared contract links | Focused checks (`git diff --check`, targeted docs/script checks) |
| Standard | Most AI-system maintenance tasks that touch multiple docs/instructions | Shared contract + affected provider surfaces + nearest index docs | Targeted functional checks plus drift/alignment checks |
| Deep Review | Cross-provider policy updates, orchestration changes, broad validation failures, or safety-sensitive flows | Build targeted context map across shared + provider-specific surfaces and related scripts | Expanded validation across impacted systems, with explicit risk log |

## Non-Negotiables

- Never use a lower mode to skip required safety checks, architecture constraints, or relevant validation.
- If acceptance criteria, risk, or affected scope grows, escalate mode before implementation.
- Keep logs summarized; preserve exact commands, file paths, and outcomes in handoffs.
- For multi-lane work, pair mode selection with `agent-handoff-checklist.md` and `parallel-task-manifest-template.md`.

## Mode Handoff Snippet

Use this compact snippet in handoff packets:

```text
Mode: <Lightweight|Standard|Deep Review>
Reason: <why this mode is sufficient>
Required context: <must-read files>
Optional context: <nice-to-read files>
Validation floor: <minimum commands that must pass>
Escalate if: <conditions requiring higher mode>
```
