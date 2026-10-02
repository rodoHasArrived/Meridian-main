# W10 close-readiness and mark-freshness operator acceptance

**Status:** preparation complete; live execution and operator decisions pending

**Owner:** Accounting and Ledger / Workstation Shell and UX / designated operator

**Reviewed:** 2026-10-02

This packet prepares the remaining `W10-SEAM-001` and `W10-MARK-001` acceptance session.
It records no operator approval. Both registry rows remain `in_progress` until their
commit-bound live evidence and explicit operator decisions satisfy their criteria.
The candidate was rebased onto source baseline `b8360e682efb8c07c46471388aacf67384c105a5`.
Pin the final candidate after the authorized lot-amortization changes and required checks;
historical hosted results in the registry do not certify that candidate.

The user's work queue remains **LOT → MARK → SEAM → reconciliation**. Within the joint
operator session, run **SEAM before MARK**, preserving the recorded desktop sequencing
handshake. This session order does not change the user's implementation priority, registry
ranking, or dependencies. See the [implementation queue](../product/implementation-todo-list.md)
and the Tier 5 section of the [recorded session order](../product/plans/next-work-determination-2026-09-27.md).

## Candidate and environment record

Complete this record before either workstation walkthrough. Use an isolated non-production
dataset provisioned through the maintained application services and approved fixture tools.
Do not present a UI mock, copied test response, or arbitrary GUID as live retained evidence.

| Field | Required retained value | Session value |
| --- | --- | --- |
| Candidate | Full commit SHA, branch/PR, clean build identity; same SHA for host and both clients | Pending |
| Hosted checks | Same-commit run URLs, selected jobs, attempts, TRX/report artifacts, failures and skips | Pending |
| Environment | Windows version, browser version, WPF installed/build version, host URL, PostgreSQL version, storage profile | Pending |
| Authentication | Tenant/company and operator identities/roles; reference credential source without copying credentials | Pending |
| Scope | Fund profile, ledger book, fund account, entity, period, book currency and period version | Pending |
| Workflow | Retained close workflow ID/version, close plan ID/version, report package ID/revision | Pending |
| Dataset | Fixture/provisioning revision, input hashes, captured evaluation time/time zone, reset procedure | Pending |
| Population | Declared schedules/accounts/positions, selection method, total denominator, exclusions and coverage limits | Pending |
| Policy | Freshness policy version and configured confidence/coverage/date/age gates; contributor freshness rules | Pending |
| Evidence location | Retained package/manifest ID and location, screenshot filenames, response/evidence identifiers | Pending |
| Operator | Name/identity, session start/end UTC, explicit per-row decision and rationale | Pending |

Declare one full close scope and use it unchanged in browser and WPF. Retain a separate
foreign-scope fixture for refusals. The representative mark population must enumerate every
included schedule and position with its book/account/security identity, valuation date and
snapshot owner; explain excluded populations rather than silently reducing the denominator.
Existing unit-test fixture identities describe scenario shapes and are not a provisioned
acceptance population.

## Shared requests and retained evidence

Use the authenticated browser and WPF clients against the same candidate host. Retain
request/response bodies with secrets removed, UTC timestamps, actor identity, HTTP status,
returned scope, policy versions, workflow/plan versions and source record IDs.

- Close projection: `GET /api/workstation/operations/financial-operations-command-center`
  with explicit `fundProfileId`, `ledgerBookId`, `fundAccountId`, `entityId` and `periodId`.
  Compare the returned `closeReadiness.scope`, contributors, blockers, evaluated time and
  active workflow identity/version in both clients. Every blocker must show type, count,
  severity, owner, causing records and an actionable repair path.
- Freshness preview: `POST /api/ledger/journal-automation/daily-mark-to-market-preview`
  using the shared `RunDailyMarkToMarketDraftIntakeRequest`. For retained schedules, use the
  schedule's exact scope/currency and `scheduleId`, and its intended valuation time as `asOf`.
  The server resolves owned positions and authenticated tenant/company; retain refusals as
  well as successful `ValuationFreshnessPreviewDto` responses. The preview creates no draft,
  approval, schedule or journal.
- Compare `assessedPositionCount`, `blockedPositionCount`, `affectedValuationCount`,
  `evaluatedAtUtc`, `policyVersion`, and each position's symbol/security/account,
  `valuationDate`, `observedOn`, `ageDays`, `status` and `blockReason`.
  Sum counts across the declared schedules, recording overlap and distinct-position counts;
  a single schedule preview must not be described as population-wide evidence.
- Retain close command and subsequent read results through the existing Accounting Close
  controller/approval lane. A ready projection alone does not authorize a close. Material
  commands require the designated human operator and the existing maker/checker controls.

The authoritative contracts and endpoints are
[CloseReadinessDtos](../../src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs),
[MarkFreshnessDtos](../../src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs),
[close readiness endpoint](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseReadiness.cs),
and [journal automation endpoint](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.JournalAutomation.cs).

## SEAM walkthrough — execute first

Open browser Accounting close/Operations Continuity and WPF Fund Ledger/Accounting Close.
Select the recorded complete scope in each. For every row below: retain the fixture change,
both rendered screens, shared response, attempted action result where applicable, repair,
refreshed result and operator verdict. Change test data through its owning service; do not
edit retained evidence in place or manufacture a ready flag.

| Case | Reproducible exercise | Required result and repair evidence |
| --- | --- | --- |
| S1 Complete current scope | Load current owned contributions and retained plan/report support for the declared workflow | Both clients consume the same complete shared decision and IDs. Capture contributor posture and the current plan/version. |
| S2 Missing dimension | Omit each scope dimension in turn or clear its UI selection | Readiness is incomplete/blocking and close is unavailable; selecting the full retained scope repairs it. |
| S3 Mismatched ownership | Substitute foreign account, book or entity while retaining other dimensions | No cross-scope evidence contributes to ready status. Show denial/blocker and restore authoritative membership. |
| S4 Missing contributor | Remove a required contribution in the isolated fixture | Show its blocker, count, severity, owner and records; retain repaired contributor evidence and refresh. |
| S5 Stale contributor | Advance evaluation time or use explicitly stale retained support | Show the stale contributor and review/refresh route; retain newly evaluated support before readiness recovers. |
| S6 Unavailable/failing contributor | Make one contributor unavailable using a controlled fixture failure | Projection remains incomplete/blocking; recover the service, refresh and retain both decisions. |
| S7 Concurrent evidence change | Review readiness, then change a retained contribution or report revision through its owner before close | Governed close/publication refuses the changed state; rebuild support and repeat affected review/approvals. |
| S8 Selection/delayed response | Delay the old request, change scope/workflow and let the old response complete | Browser and WPF invalidate the old decision; no ready/action state transfers to the new selection. |
| S9 Governed close and reload | With repaired current support, have the authorized human complete the existing close path and reload both clients | Same period/workflow outcome and retained close evidence survive reload. Record denied-role and stale-version refusals separately; retain human command and approval IDs. |

## MARK walkthrough — execute after SEAM

Open browser Portfolio/Accounting valuation preview and WPF account/aggregate position
inspectors. Use the declared population, policy and valuation dates. For each case inspect
all affected positions in both workstations; compare server-assessed status and reasons.
Retain the offending observation and policy identity, the repair input and refreshed result.

| Case | Reproducible exercise | Required result and repair evidence |
| --- | --- | --- |
| M1 Fresh mark | Supply an owned complete mark within policy and at or before valuation date | Both clients display observation date, age and policy version; preview agrees with the server's current status. |
| M2 Stale mark | Use an observation older than the permitted age | Review-required position and explicit stale reason; refresh owned mark evidence and demonstrate recovery. |
| M3 Future mark | Use an observation after valuation date | Review required with future-date reason; correct the retained observation, then refresh. |
| M4 Missing date | Supply mark evidence without an observation date | Date/age remain visibly unknown and the valuation blocks; retain a dated replacement and reassess. |
| M5 Incomplete coverage | Omit a required position's mark or completeness evidence | Show affected positions and coverage reason; restore full declared coverage before proceeding. |
| M6 Low confidence | Supply confidence below the configured minimum | The non-age confidence gate blocks with a review reason even when the mark is recent; repair through approved source evidence. |
| M7 Population preview | Run the read-only preview for every declared representative schedule/account | Retain counts/reasons and distinct population denominator, including refused scopes and exclusions. Compare preview totals to enumerated inputs. Confirm no new drafts/journals. |
| M8 Changed selection/policy | Run preview, then change schedule/scope or policy and complete an older delayed request | Old preview becomes invalid for the selection; refresh produces the current scope/policy assessment. Both clients continue to use shared decisions. |
| M9 Retained evidence/recheck | Prepare a supported valuation through the governed workflow, then stale/change its support before human approval; repair, review and reload | Current evidence is rechecked and changed support refuses. After supported repair, retained mark lineage survives reload and ties to the approved valuation/journal. No standing override bypasses the position/date/policy gates. |

## Results, defects and explicit decisions

Store one result row per case and workstation in the retained evidence package. Screenshots
must show the selected scope, status, named blockers/positions and repair route. Record
reproducible steps and source fixture revision alongside response and evidence identifiers;
screenshots alone do not establish durability or authorization.

| Case/client | Candidate/time | Evidence IDs and screenshot paths | Actual result | Defect/repair and regression evidence | Operator verdict |
| --- | --- | --- | --- | --- | --- |
| Pending | Pending | Pending | Not executed | Pending | Pending |

| Roadmap row | Decision (accept/reject/defer) | Operator identity/time | Accepted criteria or specific remaining criteria | Evidence package |
| --- | --- | --- | --- | --- |
| W10-SEAM-001 | Pending | Pending | Live browser/WPF session and explicit decision outstanding | Pending |
| W10-MARK-001 | Pending | Pending | Live browser/WPF session, population preview and explicit decision outstanding | Pending |

Fix discovered defects in focused changes, add relevant regressions, rerun the affected live
cases on the new candidate and record the new SHA. Keep either row open if any criterion is
unmet. Automated success and an agent's preparation/review are not operator acceptance.

## Automated support and validation limits

Run the maintained repository CI plus the focused suites on the candidate, and retain the
actual reports. These commands select existing automated coverage; they do not record a
live walkthrough or operator approval:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ValuationFreshnessAcceptanceTests|FullyQualifiedName~FinancialOperationsCommandCenterReadService|FullyQualifiedName~CloseReadinessSubjectSourceTests|FullyQualifiedName~WorkstationEndpointsTests|FullyQualifiedName~DailyValuationPositionServiceTests"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MarkFreshnessPresentationTests|FullyQualifiedName~AccountingCloseViewModelTests|FullyQualifiedName~AccountingCloseHttpRecoveryTests"
```

From `src/Meridian.Ui/dashboard`, after the maintained dependency install:

```text
npm run test:vitest -- src/screens/accounting-screen.mark-preview.test.tsx src/screens/portfolio-screen.mark-freshness.test.ts src/screens/accounting-screen.close-cockpit.view-model.test.ts src/screens/operations-continuity-screen.test.tsx
```

Retain the installed Windows/WPF acceptance result and same-commit hosted PostgreSQL
certification separately. Component, source-binding and view-model tests establish bounded
automated behavior; actual WPF rendering, browser interactions and the representative
population still require this recorded session.

## Queued reconciliation preview consistency

Keep the `W10-RECON-002` preview-consistency slice behind the user's LOT → MARK → SEAM
queue and existing reconciliation-lineage work in [PR #3004](https://github.com/rodoHasArrived/Meridian-main/pull/3004).
Do not activate clustered bulk UI or claim full row closure from this brief.

1. Persist each authenticated dry run's tenant/company and book/account scope, exact selected
   break IDs/versions and exact action/evidence/approval inputs as a durable preview receipt.
2. Extend the shared browser/WPF execution contract to reference that receipt. Before any
   mutation reject changed membership, versions, scope or action inputs and require a fresh
   preview. Freshly loaded versions cannot substitute for the reviewed versions.
3. Preserve the 100-case bound, per-case authorization/governance, durable audit and
   idempotent execution receipts. Recover the same result after exact retry or restart.
4. Add repository and endpoint cases for unchanged execution, replay, concurrent edits,
   substituted membership, altered inputs, cross-tenant reuse and durable reconstruction.

Acceptance requires one execution of unchanged reviewed state and zero case mutations for
stale/substituted preview state. The baseline gaps are in the
[bulk contracts](../../src/Meridian.Contracts/Workstation/ReconciliationDtos.cs) and
[file queue repository](../../src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs).

Preparation validation: source contracts, registry criteria and the recorded queue/session
order were inspected. This packet does not claim application execution, live screenshots,
population assessment, installed WPF acceptance or human approval. Record completed checks
and limitations in the pull request and the filled session record when they actually run.
