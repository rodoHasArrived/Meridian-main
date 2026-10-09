# W10 close-readiness and mark-freshness operator acceptance

**Status:** supporting evidence bound; live population and operator decisions pending

**Owner:** Accounting and Ledger / Workstation Shell and UX / designated operator

**Reviewed:** 2026-10-09

**Current integration boundary — 2026-10-09:** [PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108) is integrating main `211985830408ea2dbda618aa165e5183dc5360bb` with the SEAM repairs. The review scope includes complete approval identities and checklist acknowledgements in both clients' publication-evidence comparisons, browser timestamp equality across equivalent UTC offsets without losing 100-nanosecond precision, and aligned W03G command-center/readiness fixtures. Source, test and browser bundle changes after `12565b9` require their own validation; the four retained packets below remain historical evidence for their recorded candidates. At this boundary update, proof for the combined integration has not yet been retained here. Read current full CI and required hosted browser/Windows outcomes on the PR's current head. This note supplies no integration-gate pass or live/operator acceptance. All operator decisions remain pending, and the joint live session remains SEAM before MARK. Implementation order remains LOT → MARK → SEAM → RECON; this integration does not establish LOT/MARK queue completion.

**Historical supporting-evidence candidate — 12565b9:** [The reviewed-source packet](evidence/w10-seam-reviewed-20261007/README.md) binds 74 passing focused browser tests, 24 passing rendered response simulations and the first-attempt W03G capture to `12565b938fff633cac2060b844487c4cf9454099` on [PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108). WPF compilation records 27 compiled regression cases and zero runtime tests; its seven build-time source/test hashes match this candidate. The packet records source, tests and bundle tree identities for that candidate; it does not certify later changes to those trees. Current full CI and required hosted Windows results belong to the PR's current head; this packet asserts no full-gate pass or live acceptance. All operator decisions below remain pending, with SEAM before MARK in the joint live session.

**Windows validation history:** [Run 37645995664](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37645995664) on `495b3939` exposed a read-only `OwnerLabel` binding crash, repaired with OneWay bindings. The `23d390b32` follow-up passed 2,359 tests with one failure and one skip: inline Runs left the test's `TextBlock.Text` surface blank; that observation alone did not establish absent visible glyphs. Direct OneWay `TextBlock.Text`/`StringFormat` and contributor/code `MultiBinding` repaired the text surface. [Run 37651053647](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37651053647) associated with PR head `faf90ecf` reports tested commit `82118d79` and passed 2,360 tests with zero failures and one skip, including shared-blocker rendering. That historical run predates the additional selection/scope/evidence fixes in `12565b9`; current Windows outcomes are on [PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108). No live or operator verdict is recorded.

**Historical browser boundary — 7cbdaff:** [The earlier 2026-10-07 browser reproduction](evidence/w10-seam-merge-20261007/README.md) retains its 58 focused tests and 16 rendered simulations bound to `7cbdaff7293f91f9d486275e6386ced11f8e893b`. Its source/dependency hashes and whole-tree identities describe that earlier candidate and do not certify subsequent WPF or browser repairs.

**Historical candidate boundary — 2026-10-06:** The [2026-10-06 SEAM continuation](evidence/w10-seam-refresh-20261006/README.md) binds B1/D1 source to candidate `2f3bef7feb23f5fa9be1a9ad27dd3d08b9a5a732` on `codex/w10-seam-refresh` (base `9c96c3f72dd60111b62b8368d29e553ec58fd7a0`). Shared fixtures passed 550 tests and full Windows-target WPF compilation passed with zero runtime tests. The earlier 58 browser tests/build and 16 response simulations used an uncaptured pre-clean-install dependency environment. After `npm ci`, 37 focused view-model tests passed, but screen CSS imports and the build failed on inherited Tailwind 4/PostCSS integration. Full CI attempt 3 remains FAILED despite all 19 .NET suites passing (18,973 tests, five skipped); source-hash binding does not establish current-lock browser reproducibility. Required hosted checks and live browser/Windows operator acceptance remain pending. The archived `w10-615abde9` evidence, failures and undecided operator records below remain unchanged. LOT/MARK queue completion is not established: PR #3050 was a partial LOT slice and follow-up work remains. Preserve implementation LOT → MARK → SEAM → RECON and SEAM before MARK in the joint live session.

This packet binds the remaining `W10-SEAM-001` and `W10-MARK-001` acceptance preparation to
candidate **`615abde90001ab33bd6e58e545edc7fce635e254`**, tree
`9372fc96135e0203c0eb25fc105ed67b1d90f231`, on
[PR #3048](https://github.com/rodoHasArrived/Meridian-main/pull/3048), branch
`codex/meridian-next-tasks`. It records no operator approval. Both registry rows remain
`in_progress`. Evidence from another SHA cannot certify this candidate.

The user confirmed during this preparation that there is **no existing non-production
accounting population or Windows operator session**. The population definition below is a
provisioning specification, with returned retained identities still pending. Browser response
simulation, component tests and source inspection are identified as supporting evidence.
They cannot establish a retained population, live WPF rendering, close authorization or a
human decision. Documentation/evidence commits appended to this PR do not change which
application candidate was exercised; application changes require a new packet and rerun.

The user's implementation queue remains **LOT → MARK → SEAM → RECON**. Within the joint
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
| Candidate | Full commit SHA, branch/PR, clean build identity; same SHA for host and both clients | `615abde90001ab33bd6e58e545edc7fce635e254`; application checkout initially clean; host and installed WPF identity pending |
| Hosted checks | Same-commit run URLs, selected jobs, attempts, TRX/report artifacts, failures and skips | [Meridian CI run 37070304026](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37070304026): quality/integration gates failed; docs generation drift; .NET/PG/Windows execution cancelled. Full snapshot retained in the evidence package. |
| Environment | Windows version, browser version, WPF installed/build version, host URL, PostgreSQL version, storage profile | Debian 13 / Chromium supporting exercise; Windows, WPF, authenticated host and PostgreSQL session pending |
| Authentication | Tenant/company and operator identities/roles; reference credential source without copying credentials | Pending |
| Scope | Fund profile, ledger book, fund account, entity, period, book currency and period version | Pending |
| Workflow | Retained close workflow ID/version, close plan ID/version, report package ID/revision | Pending |
| Dataset | Fixture/provisioning revision, input hashes, captured evaluation time/time zone, reset procedure | `w10-representative-population-v1`; planned fixed valuation date `2026-10-02`, evaluation `2026-10-02T21:00:00Z`; fixture inputs and artifacts hashed in manifest; retained provisioning pending |
| Population | Declared schedules/accounts/positions, selection method, total denominator, exclusions and coverage limits | Two owned accounts, two schedules, eight distinct account/security positions, plus a separate foreign-scope refusal population; see definition below. Provisioned denominator remains pending. |
| Policy | Freshness policy version and configured confidence/coverage/date/age gates; contributor freshness rules | Candidate default `mark-freshness-v1`: max age 3 days, minimum Medium confidence, complete coverage and observation date required. Installed configuration/version and contributor rule capture pending. |
| Evidence location | Retained package/manifest ID and location, screenshot filenames, response/evidence identifiers | Pending |
| Operator | Name/identity, session start/end UTC, explicit per-row decision and rationale | Pending: no designated operator/session; no accept/reject/defer decisions supplied |

Declare one full close scope and use it unchanged in browser and WPF. Retain a separate
foreign-scope fixture for refusals. The representative mark population must enumerate every
included schedule and position with its book/account/security identity, valuation date and
snapshot owner; explain excluded populations rather than silently reducing the denominator.
Existing unit-test fixture identities describe scenario shapes and are not a provisioned
acceptance population.

## Representative population definition — provisioning pending

The versioned [population manifest](evidence/w10-615abde9/population.json) declares logical
keys; every database, workflow, plan, report, schedule and security ID must be replaced by
the owning service's actual returned identity before live execution. Logical names are not
retained evidence. Use a dedicated non-production tenant/company, USD book, one entity and
the October 2026 period. Give both schedules the same fixed valuation date and explicit
book/account scope; bind one full account scope at a time in both clients.

| Logical account / schedule | Included position keys and initial mark conditions | Denominator |
| --- | --- | --- |
| `ACCOUNT-A` / `SCHEDULE-A` | `A-FRESH` age 0 High; `A-BOUNDARY` age 3 Medium; `A-STALE` age 4 High; `A-MISSING-DATE` price present, observed date unknown | 4 distinct positions / 1 valuation batch |
| `ACCOUNT-B` / `SCHEDULE-B` | `B-FUTURE` age -1 High; `B-LOW-CONFIDENCE` age 0 Low; `B-MISSING-MARK` no quote; `B-FRESH` age 0 High | 4 distinct positions / 1 valuation batch |
| Separate foreign tenant/company/book/account/entity | At least one valid mark and one complete close workflow; use only for ownership refusals | Excluded from owned denominator; every refusal retained |

This is a stratified adversarial population covering the admission branches, a permitted
age boundary and recovery controls; it is not statistically representative of production
holdings. Reuse one security across the two accounts to prove account-specific identity:
eight distinct account/security positions, seven distinct securities, two schedules and
two valuation batches, with no schedule overlap. Expected unrepaired assessment is eight
assessed positions, five blocked positions and two affected valuation batches. Age-3/Medium
is admissible at the candidate default; stricter installed settings require recalculating
the expectation and recording that policy. Missing dates and quotes are not assigned age 0.

Enumerate **every** position owned by each schedule through `DailyValuationPositionService`
and retain the snapshot/position owner. Added/unexpected positions change the denominator
and invalidate the bound preview. Explicit exclusions are production holdings, foreign
scopes, unsupported instruments, schedules outside these accounts, and any unprovisioned
identity. After replacing every blocked mark with owned dated High-confidence evidence,
the planned recovery expectation is eight assessed, zero blocked and zero affected batches.
Neither expectation is an observed live result.

Provision accounts/books/securities through their maintained owners; acquire holdings through
the governed accounting/lot path, create the two retained mark schedules and input observations,
and retain a complete close workflow, scoped report revision and real plan evidence for each
account. Retain same-scope carrying-value/balance results for all eight account/security keys:
the mark service loads these before assessing freshness, so missing carrying values refuse the
entire preview and must be recorded as unassessed, rather than zero blocked. Capture quantity,
cost basis, quote price/source/currency and the owned snapshot/hash for each position. The mark
source resolves by symbol/as-of, so the security shared across accounts uses the same fresh
observation in both; account identity still distinguishes the two positions.
The maintained demo seeder is seeded support and lacks the required complete close
scope; do not adopt its report as acceptance authority. The [criterion/provisioning review](evidence/w10-615abde9/criteria-review.md)
names the owners and missing seed dimensions. Snapshot the isolated storage roots/database
after provision and before each fault, record hashes/IDs/versions, and repair by appending
replacement evidence through its owner. Reset by restoring that isolated baseline before
the next case; preserve the prior case package. Never edit evidence records to force readiness.

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
| S4b Unregistered contributor | Remove the required lane registration in an isolated host profile, leaving its data present | Incomplete/blocking projection explicitly names the unregistered lane; restoring registration and reevaluating repairs it. Missing data alone does not exercise unregistered service composition. |
| S5 Stale contributor | Advance evaluation time or use explicitly stale retained support | Show the stale contributor and review/refresh route; retain newly evaluated support before readiness recovers. |
| S6 Unavailable/failing contributor | Make one contributor unavailable using a controlled fixture failure | Projection remains incomplete/blocking; recover the service, refresh and retain both decisions. |
| S7 Concurrent evidence change | Review readiness, then change a retained contribution or report revision through its owner before close | Governed close/publication refuses the changed state; rebuild support and repeat affected review/approvals. |
| S8 Selection/delayed response | Delay the old request, change scope/workflow and let the old response complete | Browser and WPF invalidate the old decision; no ready/action state transfers to the new selection. |
| S9 Governed close and reload | With repaired current support, have the authorized human complete the existing close path and reload both clients | Same period/workflow outcome and retained close evidence survive reload. Record denied-role and stale-version refusals separately; retain human command and approval IDs. |
| S10 Coverage vocabulary | Inspect asset-class coverage beside the shared scoped close decision in both clients | Coverage remains diagnostic and cannot enable close or be labelled close readiness. Retain simultaneous coverage/close states and the operator's interpretation. |

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
| M10 Override expiry and binding | On a candidate with a supported governed override, use one reviewed position/observation/valuation-date/policy tuple, then expire it at time of use and substitute each binding in turn | Expired or substituted authority refuses; fresh independent review or corrected owned evidence repairs it. **Blocked at this candidate:** there is no retained override/expiry contract. Inert override metadata refusal is supporting evidence only. Keep MARK criterion 4 pending; no acceptance by treating this case as passed or not applicable. |
| M11 Full refusal and repair chain | Attempt valuation with each population defect, capture refusal and zero draft/approval/journal mutation; append correct owned support, preview all schedules again and repeat maker/checker review | Bind the refusal, repair input, new policy assessment, command result and reload lineage to the same declared subject. Material commands and verdict require the designated human; no operator action was executed during preparation. |

## Results, defects and explicit decisions

Store one result row per case and workstation in the retained evidence package. Screenshots
must show the selected scope, status, named blockers/positions and repair route. Record
reproducible steps and source fixture revision alongside response and evidence identifiers;
screenshots alone do not establish durability or authorization.

| Case/client | Candidate/time | Evidence IDs and screenshot paths | Actual result | Defect/repair and regression evidence | Operator verdict |
| --- | --- | --- | --- | --- | --- |
| Browser supporting exercise | `615abde9`; `2026-10-02T22:26:25.450Z`–`22:27:10.384Z` | [Browser evidence](evidence/w10-615abde9/browser/browser-evidence.md): 43 screenshots and 173 target request/response bodies | 37 rendered response-simulation observations: 36 bounded checks passed, B1 repair failed; 62 component tests and TypeScript/Vite build passed | B1 remains open; simulation does not establish retained evidence | Pending |
| Desktop inspection | `615abde9`; `2026-10-02T22:10:31Z` | [Desktop evidence](evidence/w10-615abde9/desktop/desktop-evidence.md), source hashes and reproducible Windows commands | Eight maintained static structure checks passed; zero Windows tests or rendered desktop cases executed | D1 blocker fields flattened; D2 schedule preview absent; D3 override lifecycle absent | Pending |
| Shared service support | `615abde9`; TRX timestamps | [Candidate TRX and report](evidence/w10-615abde9/server/server-evidence.md) | 570 passed, zero failed/skipped; refusal/repair coverage and actual test names retained | Test doubles and ephemeral fixtures do not establish the live population or maker/checker session | Pending |
| Every live case/client | Candidate specified; no session time | [Case ledger](evidence/w10-615abde9/case-records.json) lists all 44 browser/desktop rows, evidence tiers and missing proof | Pending: authenticated retained host, provisioned population and designated operator unavailable | Provision/run the listed cases; preserve refusals, append owned repairs, reload and collect decisions | Pending |

### Per-criterion decisions

Each criterion is copied verbatim from the candidate registry in the machine-readable
case ledger and mapped to candidate-bound sources/tests in the
[criterion review](evidence/w10-615abde9/criteria-review.md). **Pending is an explicit
undecided state**, not an operator's accept/reject/defer verdict. No operator identity,
decision time or rationale has been invented. Supporting engineering checks do not close
any criterion requiring both rendered clients, population certification or human review.

| Criterion | Reproduction and supporting evidence | Explicit decision / remaining gate |
| --- | --- | --- |
| SEAM-C1 Shared authority and complete scope | S1–S3/S8; shared scoped service tests; browser and desktop consumer inventories | Pending — full owned scope, both live consumers and operator review |
| SEAM-C2 Blocker type/count/severity/owner/records | S4/S4b/S5/S6; shared contract/service tests; desktop D1 | Pending — verify structured shared blocker details in both clients; D1 remains open |
| SEAM-C3 Missing/failing/stale/out-of-scope lane blocks | S3–S7 plus S4b; controlled fault/refusal and repair tests | Pending — live lane faults and registration change, retained repair evidence, both clients |
| SEAM-C4 Contributor and coverage vocabulary | S1/S10; candidate contributor paths and diagnostic coverage review | Pending — simultaneous rendered coverage/close states and operator interpretation |
| SEAM-C5 Desktop sequencing handshake | Recorded alignment plan and separate SEAM-before-MARK session order | Pending — installed desktop acceptance and explicit criterion decision |
| SEAM-C6 Concrete linked implementation/evidence | Candidate source/test links, check snapshot, result ledger and reproduction commands | Pending — same-candidate required gates and human criterion review |
| MARK-C1 Age/date admission blocks by default | M1–M4/M9/M11; `ValuationFreshnessAcceptanceTests` | Pending — owned population refusal/repair chain and operator review |
| MARK-C2 One policy preserves all stricter gates | M4–M6/M8; compatibility, confidence, coverage and policy-tamper tests | Pending — installed policy capture, full coverage and both live clients |
| MARK-C3 Offending positions render review required | M2–M7/M11; browser tests/captures and WPF presentation bindings | Pending — actual population/reasons rendered in Windows and browser |
| MARK-C4 Date/age and bound expiring override | M1–M10; presentation inventory and inert-tag/tamper refusal; desktop D3 | Pending — override lifecycle/expiry is unimplemented at this SHA; refusal of tags does not satisfy expiry |
| MARK-C5 Preview before enabling default | M7/M8; two-schedule manifest, exact denominator and preview count tests | Pending — provisioned full-population preview, activation timing and human decision; desktop D2 remains open |
| MARK-C6 Concrete linked implementation/evidence | Candidate source/test links, same-candidate report and both surface inventories | Pending — live evidence, required gates and explicit operator criterion review |

### Open findings and actual decisions

- **B1 — Browser repair retains old detail version:** the rendered response simulation
  changes the selected workflow version, then supplies repaired ready support. Refreshing
  the workflow list/shared projection retains the older selected detail version, so
  publication stays disabled. Capture and exact request ordering are in the browser
  report. This is a bounded rendered finding; live reproduction remains pending.
- **D1 — Shared blocker detail:** desktop shared projection presentation flattens fields to
  contributor/message, while legacy blocker rows do not prove that the shared type, count,
  severity, owner and causing record IDs are visible. Source-supported finding; rendered
  confirmation pending.
- **D2 — Desktop population preview:** account/aggregate freshness inspectors consume shared
  assessments, but no desktop schedule-preview call was found. Inspector parity cannot
  establish M7/M8. Source-supported finding; Windows confirmation pending.
- **D3 — Override expiry:** there is no implemented retained mark-override lifecycle or expiry
  contract. Preserve M10 and MARK-C4 as pending. Implementing this would change the candidate
  and belongs in the authorized implementation queue, followed by fresh evidence.
- **C1 — Candidate checks:** [Meridian CI 37070304026](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37070304026)
  failed quality/integration gates after docs health drift, and Windows execution was
  cancelled. The documentation generator is rerun in this preparation change; new hosted
  results must be assessed separately and cannot rewrite this candidate snapshot.

The only actual user input on session readiness was that no existing population or Windows
session is available. It is recorded as an environment fact, not financial approval.
The agent's evidence-retention decision is to leave all 12 criteria and both row decisions
pending. No governed close, valuation submission, approval, posting, override decision or
default activation was made on behalf of an operator.

| Roadmap row | Decision (accept/reject/defer) | Operator identity/time | Accepted criteria or specific remaining criteria | Evidence package |
| --- | --- | --- | --- | --- |
| W10-SEAM-001 | Pending | None supplied | SEAM-C1 through C6; live browser/WPF session and explicit decisions outstanding | [Candidate evidence package](evidence/w10-615abde9/README.md) |
| W10-MARK-001 | Pending | None supplied | MARK-C1 through C6; population preview, live Windows rendering, override expiry and explicit decisions outstanding | [Candidate evidence package](evidence/w10-615abde9/README.md) |

Fix discovered defects in focused changes, add relevant regressions, rerun the affected live
cases on the new candidate and record the new SHA. Keep either row open if any criterion is
unmet. Automated success and an agent's preparation/review are not operator acceptance.

## Automated support and validation limits

Run the maintained repository CI plus the focused suites on the candidate, and retain the
actual reports. These commands select existing automated coverage; they do not record a
live walkthrough or operator approval:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ValuationFreshnessAcceptanceTests|FullyQualifiedName~FinancialOperationsCommandCenterReadService|FullyQualifiedName~CloseReadinessSubjectSourceTests|FullyQualifiedName~WorkstationEndpointsTests|FullyQualifiedName~DailyValuationPositionServiceTests"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~OperationsContinuityViewModelTests|FullyQualifiedName~AccountingCloseViewModelTests|FullyQualifiedName~AccountingCloseHttpRecoveryTests|FullyQualifiedName~MarkFreshnessPresentationTests"
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

Preparation validation is retained in the candidate evidence package: 570 shared-service
tests, 62 browser component tests, the TypeScript/Vite build, eight WPF static checks and
rendered Chromium response-simulation captures. Required repository CI is recorded separately.
The packet does not certify an authenticated retained population, actual Windows rendering,
governed command execution or human approval. Preserve those missing proofs and undecided
gates in the pull request and session record.
