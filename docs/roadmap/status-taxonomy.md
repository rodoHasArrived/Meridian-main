# Roadmap Status Taxonomy

## Status values

| Status | Meaning |
| --- | --- |
| `planned` | Work is accepted into the roadmap but implementation is not actively underway. |
| `in_progress` | Work is active and should have an owner lane plus narrow validation guidance. |
| `ready_for_acceptance` | Implementation evidence exists, but operator or governance acceptance is not complete. |
| `accepted` | Acceptance evidence exists and is linked from the roadmap item. |
| `done` | Work is closed with evidence and any required release or status documentation. |

## Health values

| Health | Meaning |
| --- | --- |
| `green` | No known blocker for the current target. |
| `yellow` | Watch item or unresolved acceptance gap exists. |
| `red` | Known blocker prevents acceptance. |
| `blocked` | Work cannot proceed until the blocker is resolved. |

Legacy values `on_track`, `watch`, and `at_risk` are accepted during migration for compatibility with existing status language.

## Mapping to the solo-developer completion vocabulary

[Solo-developer workflow](../ai/solo-developer-workflow.md#completion-vocabulary) uses Planned, In progress, Verified, and Released for human communication. This mapping is communication guidance only; it makes no schema change and does not alter any registry status or health value.

| Registry status | Solo-developer wording |
| --- | --- |
| `planned` | Planned |
| `in_progress` | In progress |
| `ready_for_acceptance`, `accepted` | Verified for the stated scope; the recorded implementation and evidence posture decides, and the scope and unresolved limitations must be stated. Operator or governance acceptance is a separate fact, not a precondition for development verification. |
| `done` | Verified; Released only when included in an identified supported release. |

Blocked is not a status. Express it through `health: blocked` or `health: red` and state the blocker in prose.
