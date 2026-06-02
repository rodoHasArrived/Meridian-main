# AI Work Modes

Use these modes to right-size context and validation effort without skipping safety or correctness.
Pair mode selection with the active entry in `docs/ai/model-routing-policy.json` when a task requires explicit model-class routing.

## Mode Selection

| Mode | Use when | Context scope | Validation expectation |
| --- | --- | --- | --- |
| Lightweight | Small docs edits, narrow prompt tweaks, localized AI guidance touch-ups | Read only directly affected files plus shared contract links | Focused checks (`git diff --check`, targeted docs/script checks) |
| Standard | Most AI-system maintenance tasks that touch multiple docs/instructions | Shared contract + affected provider surfaces + nearest index docs | Targeted functional checks plus drift/alignment checks |
| Deep Review | Cross-provider policy updates, orchestration changes, broad validation failures, or safety-sensitive flows | Build targeted context map across shared + provider-specific surfaces and related scripts | Expanded validation across impacted systems, with explicit risk log |

## Task-Start Recipe

Before editing, record:

- the chosen mode,
- the coordinator or final integration owner,
- the smallest initial file set,
- the validation owner for the lane,
- the first proof command.

If the lane needs a script/validator choice, load `docs/ai/tooling/README.md` before widening repo context.

## Non-Negotiables

- Never use a lower mode to skip required safety checks, architecture constraints, or relevant validation.
- If acceptance criteria, risk, or affected scope grows, escalate mode before implementation.
- Keep logs summarized; preserve exact commands, file paths, and outcomes in handoffs.
- For multi-lane work, pair mode selection with `agent-handoff-checklist.md` and `parallel-task-manifest-template.md`.

## Token And Evidence Budget Heuristics

Use these defaults unless risk requires more context:

| Mode | Context budget guidance | Evidence handoff guidance |
| --- | --- | --- |
| Lightweight | Required context only (typically one owning doc or file family) | 5-10 line summary with exact commands + pass/fail |
| Standard | Required context plus one adjacent owner surface (contract/index/script) | 10-20 line summary with touched files, validation reuse notes, and open risks |
| Deep Review | Targeted cross-surface map (shared contract + affected host surfaces + validators) | Structured packet with required vs optional context and explicit rerun triggers |

If the context needed exceeds the mode guidance, escalate mode before implementation.

## Escalation Triggers

Escalate one level when any of these becomes true:

- another lane needs the same files and ownership is no longer disjoint,
- the validation floor expands beyond the lane's planned proof command,
- shared policy, handoff guidance, or generated artifacts also need to change,
- the lane can no longer summarize inspected evidence compactly without dropping required facts.

## Mode Handoff Snippet

Use this compact snippet in handoff packets:

```text
Mode: <Lightweight|Standard|Deep Review>
Reason: <why this mode is sufficient>
Required context: <must-read files>
Optional context: <nice-to-read files>
Inspected files: <what this lane already read>
Validation owner: <who reruns checks after integration>
Validation floor: <minimum commands that must pass>
Escalate if: <conditions requiring higher mode>
```
