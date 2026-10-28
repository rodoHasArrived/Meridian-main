# Workflow Correctness Consolidation

**Status:** active review record
**Owner:** Core Team
**Reviewed:** 2026-10-28

This successor incorporates main `6bcc7a0262ab0870cee32cb2cb344851e9da7e89` and retains both
original heads as merge ancestors: #3168 `a413052520f29fdb9636b0f77bce496eba43f9d6` and #3169
`8e7a6703aa40a229943f61779bad7c9542a29697`. It selects one coherent implementation of each
overlapping fix and repairs the outstanding findings. Historical results on the original branches
do not certify the consolidated tree; final integration checks remain required.

## Retained Behavior and Coverage

| Original concern | Selected implementation and proof |
| --- | --- |
| Currency-compatible reconciliation, configured tolerances and rule evidence | #3169 matching engine and workflow-policy tests. New assignment regressions cover normalized observed-price and settlement-distance ranking for transactions, positions and cash. Rule-list precedence and missing-price behavior remain explicit. |
| Retained execution price | #3168 `LedgerJournalInternalTransactionSource` and its `Price.cs` regressions. Only a single-currency share movement receives retained `tradeFill.price`; commission journals and absent prices cannot invent an execution price from net cash. |
| Signed and compound cash flows | #3169 explicit, balanced line-ID attribution, report/export tests and reversal remapping. Untagged ambiguous compound history refuses generation. An added uppercase-retained-tag case exercises the same accepted evidence through reversal. |
| Whole-record credentials through provider construction | #3169 resolver/factory/adapters and credential-isolation tests. Setup tests/status now use the same whole-record selection; partial and retained-empty records cannot borrow environment fields. A presence-aware Core compatibility method and vault adapter preserve empty versus absent selection. |
| Entity-scoped payment reads and decisions | #3169 scoped API and cancellation/session tests, plus an explicit entity selection on the normal approvals route. Unapplied scope edits disable decisions; applying a new scope clears old confirmations. |
| Account/request-scoped Cash Ladder | #3169 keyed account sessions and request-bound balances/drills. New loading/failure/recovery coverage retains same-account scenario choices independently of financial evidence. |
| Live freshness and monitoring | #3169 live-ingress publisher, subscription synchronization, periodic monitor, weekday/UTC window, alerts and regressions. UI configuration alone no longer starts monitored subscriptions or overwrites collector gauges. Closed-session histogram exclusion is retained and covered explicitly. |
| Contract array/attribute parsing | #3168 literal-aware parser handles escaped/verbatim strings, character literals, comments and array suffixes. All current-main enforcement assertions remain, including provenance contracts; both branches' useful parser cases are retained. |
| Portable migration paths | Both implementations normalize generated paths with `as_posix()`. #3168 real-CLI Windows-path regression and #3169 reservation tests are retained. External traversal protections are unchanged. |

## Deliberate Supersession

The alternate #3168 tolerance helper, freshness implementation, provider-factory wiring, payment
panel and cash-ladder implementation are superseded by the corresponding coherent #3169 paths
and the corrections above. They are alternate implementations of the same nine behaviors, rather
than additional product scope. Keeping both helpers or test suites with incompatible assumptions
would reintroduce ambiguous rule precedence or duplicate responsibilities. Their original Git
history remains reachable, and the retained tests named above cover their useful intent.

Content-hashed workstation bundles are regenerated from the combined source. Generated document
conflicts are seeded from main and regenerated; reviewed source hashes are refreshed only for
the modules changed and reviewed in this consolidation. Current contract-parser verification
enforces 109 pairs, with 33 declared divergence candidates and no undeclared problems. Older
counts and diagnostic tables on the source branches describe their historical baseline.

## Validation Boundary

Focused browser and script checks, strict TypeScript, source formatting, and rendered Chromium
checks supply local evidence. Added .NET regressions require the final combined .NET and hosted
integration gates; no full .NET build was run in this lane. Accounting review addresses the
technical treatment and compatibility refusal, without asserting production-release certification.
