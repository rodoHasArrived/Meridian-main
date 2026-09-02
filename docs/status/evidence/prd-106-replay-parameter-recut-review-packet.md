# PRD-106 Replay and Parameter Re-cut Review Packet

**Status:** review required; not completion or acceptance evidence

**Owner:** Backtesting + Strategy Analytics

**Reviewed:** 2026-09-02
**Candidate branch:** `codex/replay-parameter-fail-closed`

## Scope and claim boundary

This focused re-cut contributes two safety improvements to
[`PRD-106`](../../product/implementation-todo-list.md): deterministic, bounded JSONL replay and
fail-closed Quant Lab runtime-parameter extraction. It does not claim that browser and WPF
orchestration are unified, that the other PRD-106 recovery/outbox requirements are complete, or
that PRD-106 is closed.

The `W6-BTSTUDIO-001` registry entry remains unchanged. Its completed scope is still the bounded,
host-composed browser Covered Call evidence and governed Paper-promotion loop. This re-cut neither
reopens that row nor broadens it into a general Backtesting Studio completion claim.

## Candidate behavior under review

- JSONL and supported compressed JSONL partitions are external-sorted by full UTC timestamp, then
  ordinal file position and physical line number.
- Sort preparation takes whole-operation admission before opening a merge batch. No admission or
  file handle is retained across a public replay yield; bounded pages are materialized and closed
  before their events are emitted.
- Malformed and null source records fail closed with file and line evidence. Cancellation and early
  consumer disposal release admission, close readers, and attempt spool cleanup.
- Quant Lab ties parameter metadata to the exact current editor source. Run is disabled while
  extraction is pending or unavailable, and the command path independently rejects an attempted
  launch without current-source metadata.
- A failed parameter refresh may display the last usable descriptors and overrides as stale
  reference data, but it cannot submit them or silently fall back to inline defaults. A successful
  refresh replaces removed descriptors and retains only matching overrides.

## Required focused human review

Two independent approvals are required on the exact re-cut head. The author cannot satisfy either
approval, and one generic review does not satisfy both lanes.

| Review lane | Required focus | Result to record on the pull request |
| --- | --- | --- |
| Backtesting / Strategy Analytics | Multi-pass chronology; equal-timestamp ties; multi-symbol priming and concurrent batch liveness; cancellation; early disposal; spool cleanup; exact-source parameter/run coupling | Named reviewer, approval state, reviewed commit SHA, and link to review |
| Accounting and Ledger | No silent input loss or default substitution; replay ordering preserves auditable event causality; malformed evidence blocks downstream consumption; scope excludes lot and corporate-action accounting changes | Named reviewer, approval state, reviewed commit SHA, and link to review |

Any code change after either approval invalidates that approval until the reviewer confirms the new
head. Unresolved P1 findings block merge.

## Required fresh hosted gates

Every check below must succeed on the exact re-cut head after the final code and documentation
change. A result from an earlier #2789 head or a superseded re-cut SHA is not reusable.

| Workflow | Required jobs/checks |
| --- | --- |
| Meridian CI | `verify-dotnet`, `verify-browser`, `verify-docs`, and `quality-gate` |
| Windows Desktop Build | `verify-desktop (build/test WPF)` |
| WPF Dev Loop Validation | `WPF Dev Loop (DesktopWorkflowScriptTests)` |
| WPF Route Validation | `Position Blotter Route Validation` and `Operator Inbox Route Validation` |
| CodeQL | `Analyze csharp` and `Analyze javascript-typescript` |
| Documentation Automation | `validate-docs` and `regenerate-docs` |
| Roadmap Source Docs | `scope-gate` and `schema` |

The pull request must link each hosted run and record the tested head SHA. Skipped, cancelled,
neutral, or pending checks do not count as fresh green evidence.

## Merge guard

This packet is a review contract, not evidence that its conditions already passed. Merge remains
blocked until both focused human approvals and every fresh hosted gate above are recorded against
the same current head. The pull request must continue to describe this work as PRD-106 hardening
while preserving the bounded `W6-BTSTUDIO-001` completion claim.
