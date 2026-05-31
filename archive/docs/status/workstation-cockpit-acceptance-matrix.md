# Workstation Cockpit Acceptance Matrix

**Last reviewed:** 2026-05-28  
**Machine-readable companion:** [`workstation-cockpit-acceptance-matrix.json`](workstation-cockpit-acceptance-matrix.json)  
**Validator:** `python3 scripts/dev/validate_workstation_cockpit_acceptance_matrix.py`

This matrix maps each cockpit/governance acceptance criterion to its route or API owner,
required UI behavior/state, filterable automated coverage, and evidence artifact pointer. The JSON
companion is the source used by CI-friendly validation; update the Markdown and JSON together when a
criterion, route, test, or artifact expectation changes.

## CI acceptance rule

A criterion is invalid when any of these fields are missing or blank in the JSON companion:

1. at least one route/API owner in `routeOwnership[].route`
2. at least one automated coverage entry with both `project` and `filterableName`
3. at least one evidence pointer in `artifacts[].path`

Run the validator before claiming cockpit/governance acceptance evidence:

```bash
python3 scripts/dev/validate_workstation_cockpit_acceptance_matrix.py
```

## Matrix

| Criterion ID | Owning route/API | Required UI behavior/state | Required automated tests | Required artifacts |
| --- | --- | --- | --- | --- |
| `cockpit-trading-readiness-posture` | `/api/workstation/trading/readiness`; `/trading/readiness` | Readiness console shows Ready, ReviewRequired, Blocked, loading, and error postures with gate-level disabled reasons, work-item details, and route-aware recovery actions. | `tests/Meridian.Tests` filter `MapWorkstationEndpoints_TradingReadiness_ShouldProjectSharedReadinessAndDegradeWhenEvidenceIsMissing`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > builds the API-first readiness console from shared workstation payloads`; `tests/Meridian.FSharp.Tests` filter `Trading readiness overall posture follows canonical gate precedence` | `docs/screenshots/workstation/trading-readiness.png`; `artifacts/provider-validation/_automation/2026-04-27/dk1-pilot-parity-packet.json` |
| `cockpit-operator-inbox-routing` | `/api/workstation/operator/inbox`; `/trading/readiness` | Inbox prioritizes critical rows, preserves selected detail, exposes retry when loading fails, clears stale account rows during account changes, and keeps fallback rows visible. | `tests/Meridian.Tests` filter `MapWorkstationEndpoints_OperatorInbox_ShouldProjectTradingReadinessWorkItemsWithNavigation`; `tests/Meridian.Tests` filter `MapWorkstationEndpoints_FundAccountScope_OperatorInboxScopedQueries_ShouldReturnPerAccountBrokerageSyncItemsWithoutCrossAccountLeakage`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > surfaces operator inbox failures while keeping payload fallbacks visible` | `docs/screenshots/workstation/operator-inbox.png`; `artifacts/workstation/operator-inbox/latest-response.json` |
| `governance-report-pack-evidence` | `/reporting/evidence`; `/reporting/report-packs` | Evidence links preserve `subjectKind`/`subjectId`; report-pack UI distinguishes missing payload, in-review, approved, released, and export-busy states. | `src/Meridian.Ui/dashboard` filter `workspace.test.ts > builds encoded Evidence Workbench subject routes`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > keeps the headline in review when governed report-pack readiness is missing`; `src/Meridian.Ui/dashboard` filter `governance-screen.view-model.test.ts > derives reporting profile selector rows and detail state` | `artifacts/reporting/latest-report-pack-manifest.json`; `docs/screenshots/workstation/reporting-evidence.png` |
| `governance-reconciliation-break-triage` | `/api/workstation/reconciliation/cases`; `/accounting/reconciliation` | Reconciliation lane renders open-break counts, selected detail, resolve/dismiss validation, failure copy, narrative context, and evidence packet actions. | `tests/Meridian.Tests` filter `MapWorkstationEndpoints_OperatorInbox_ShouldIncludeOpenReconciliationBreaks`; `src/Meridian.Ui/dashboard` filter `governance-screen.view-model.test.ts > derives reconciliation break action state and live announcements`; `src/Meridian.Ui/dashboard` filter `governance-screen.view-model.test.ts > derives reconciliation detail actions from the selected run` | `artifacts/reconciliation/latest-break-replay.json`; `docs/screenshots/workstation/accounting-reconciliation.png` |
| `cockpit-fund-account-context-continuity` | `/api/workstation/trading/readiness?fundAccountId={fundAccountId}`; `/api/workstation/operator/inbox?fundAccountId={fundAccountId}` | Account changes trigger scoped reloads, abort superseded inbox loads, clear stale rows, and keep the visible account context attached to action metadata. | `tests/Meridian.Tests` filter `MapWorkstationEndpoints_FundAccountScope_ShouldReturnOnlyRequestedAccountWorkItemsAndPreserveRoutingMetadata`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > clears stale account-scoped inbox rows while loading a new fund account inbox`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > aborts superseded operator inbox loads when the fund account changes` | `artifacts/workstation/account-context/readiness-scoped-response.json`; `artifacts/workstation/account-context/inbox-scoped-response.json` |
| `cockpit-replay-and-promotion-continuity` | `/api/workstation/trading/readiness`; `/trading/readiness`; `/reporting/evidence?subjectKind=strategy-run&subjectId={runId}` | Replay and promotion rows expose mismatch reasons, stale replay recovery, mirrored run IDs, promotion packet action labels, and selected evidence-panel details. | `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > maps replay mismatch and stale replay gates into the normalized checkpoints`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > keeps mirrored run-handoff identity and route metadata in operator work items`; `src/Meridian.Ui/dashboard` filter `operator-readiness-console.view-model.test.ts > preserves route-aware packet continuity for blocker rows across state slices` | `artifacts/workstation/replay/latest-readiness-replay.json`; `artifacts/workstation/promotion/latest-review-packet-manifest.json` |

## Maintenance notes

- Prefer stable, filterable test names over broad project references so failures can be reproduced
  with targeted `dotnet test --filter` or dashboard test-name filters.
- Artifact pointers may reference committed evidence, generated CI artifacts, or expected acceptance
  outputs; they are pointers for acceptance evidence, not necessarily files committed to the repo.
- Add a new criterion before claiming a new cockpit/governance acceptance path in roadmap or status
  docs, then run the validator and include the command in review evidence.
