# What To Work On Next — Meridian (2026-09-27)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-27
**Baseline:** `main` at `5980fa00`
**Supersedes:** [2026-09-23](next-work-determination-2026-09-23.md)
**Method:** read the roadmap registry (`docs/roadmap/data/*.yml`), the production-readiness tracker
(`docs/product/implementation-todo-list.md`), the decision log, and live GitHub Actions and
pull-request state; verified each named roadmap remainder against current source rather than against
its own record; measured the stranded-branch queue with `git merge-tree` against `origin/main`. Every
claim is anchored to a run, a commit, a PR number, a `file:line`, or a reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate.

## Headline

**Both of the previous list's P0 recommendations landed, and one of them changed the shape of the
release gate. The remaining P0 work is no longer "prove the certification lane works" — it is cut a
release candidate, because five of the six evidence-gated P0 rows now need only the same green run on
a frozen commit, and no `v*` tag has ever existed in this repository.**

- `Production Certification` now runs on **every push to `main`** and is green on the current tip.
  The weekly-cadence risk the last document raised is closed by mechanism, not by luck.
- The pull-request queue went **43 → 25**; the stranded `codex/*` set went **22 → 11**, and **8 of
  those 11 conflict only in regenerable output**.
- Two in-progress W9 rows are substantially closer than their own records say. `W9-GOV-008`'s route
  baseline is burned down to **zero** and its file-backed audit chain exists; `W9-INGEST-009`'s live
  ledger-transaction population exists. Each now has exactly one bounded remainder, named below.

## What closed since 2026-09-23

| Previous item | Status today | Evidence |
| --- | --- | --- |
| Trigger `Production Certification` on `main` pushes | **Implemented.** `push: branches: [main]` and `tags: v*` are now triggers. | `.github/workflows/production-certification.yml:6-11`; runs #89–#95 are all `event: push` |
| Certification green on the current tip | **Green on `5980fa00`.** All four jobs; the zero-skip gate ran and passed. | [run 36323602419](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36323602419), `Reject failed or skipped deterministic tests` = success |
| Correct the "broader Production Certification remains failed" sentence on three W10 rows | **Done.** The string no longer appears anywhere in the registry. | `grep -rn "Production Certification remains failed" docs/roadmap/data/roadmap-items.yml` → no match |
| Refresh `program-state.yml` (24d stale) and the tracker (35d stale) | **Done.** | `program-state.yml:10` `snapshot_date: 2026-09-26`; tracker `Reviewed: 2026-09-23` |
| Take or decline `W9-SAFETY-007` (22 days at `ready_for_acceptance`) | **Taken.** `accepted`, with the recorded OCO and deferred risk-journal reservations. | `DEC-W9-ACCEPTANCE-004`, decided 2026-09-11, restored to the log 2026-09-23 |
| Collapse the nine duplicate "Security Master institutional-requirements pass" drafts | **Done.** All nine are closed. | #2892, #2904, #2905, #2935, #2937, #2961, #2975, #2982, #2984 absent from the open list |
| Drain the ten mechanically-mergeable stranded branches | **Five drained.** #2912, #2916, #2922, #2940, #2948 are closed; five remain open. | open-PR list below |
| Verify and close #2947, #2945 as superseded | **Done.** #2945's acceptance record was restored into the decision log rather than discarded. | `DEC-W9-ACCEPTANCE-004` summary: "Restored from the existing operator-acceptance record in PR 2945 on 2026-09-23" |
| Fund-structure cross-tenant read/write hole (`W9-GOV-008`) | **Closed in source.** The store is tenant-columned and scope-filtered. | `src/Meridian.Storage/FundStructure/Migrations/004_fund_structure_tenant_columns.sql`; `PostgresFundStructureService.Tenancy.cs`; #2956, #2959 merged |

One regression worth recording because the new push trigger is what caught it: certification
[run #88](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36207571140) **failed** on
`a03238c6` (the `PRD-001` fail-closed authentication merge, #2997) in the deterministic PostgreSQL
job. It was green again on the next merge, `f9691761` (#3000). Under the old weekly cadence that
failure would have been invisible until 2026-09-27; it was instead visible within the hour. That is
the trigger paying for itself, and it is the argument for keeping it.

## Tier 0 — Cut a release candidate. The P0 gate has no other next step.

This is the top item and it is newly cheap. Read the six evidence-gated P0 rows together and they
say almost the same sentence:

| Row | What the tracker says remains |
| --- | --- |
| `PRD-013` | "The same run repeated on the release commit. **The lane itself is no longer the blocker**; only the same-commit attachment remains." |
| `PRD-015` | "Successful dated `production-recovery-drill-*` artifact **on the release commit**" plus operator review |
| `PRD-016` | "Repeat the green hosted run **on the frozen release commit**; required-check/ruleset activation by a repository administrator" |
| `PRD-017` | "All local docs/hash validators and the hosted documentation evidence job green **on the final candidate commit**" |
| `PRD-014` | "**Protected signing secret**, an available native Windows ARM64 runner, prior release artifact, and green tag workflow" |
| `PRD-000` | "Core-team approval of ADR-019/ADR-020 and support matrix; clean installed startup/upgrade/rollback receipts **from the release commit**" |

Four of those six are discharged by one tag run. And the tag run has never happened:

```
$ git tag --list
eval-v0.1.0-eval.1
```

**One tag exists in the entire repository, and it is an eval tag.** `Desktop Installer Release`
triggers on `push: tags: v*` (`desktop-installer-packaging.yml:3-6`); `Production Certification`
now does too (`production-certification.yml:9-11`). Neither has ever run against a `v*` tag, so every
installed lifecycle receipt in the tracker was minted by a `workflow_dispatch` with a throwaway
self-signed certificate — which the workflow's own input description says is "**never** release
evidence."

### The sequence

1. **Put the signing certificate in the protected environment.** `desktop-release-signing` is the
   environment; `MDC_SIGNING_CERT_PFX_BASE64` is the secret; the tag build throws without it
   (`desktop-installer-packaging.yml:129,172,183`). This is the one hard prerequisite and it is not
   engineering work — it is a credential someone has to provide. **Nothing else in Tier 0 can start
   until it exists.**
2. **Decide the ~1 GB consumer installer question first.** `Meridian-Setup.exe` was 1,043,350,783
   bytes because it carries both architectures plus a full PostgreSQL distribution. The tracker
   already flags it as "a product decision to revisit." Revisit it *before* the tag, not after —
   an RC that ships a 1 GB download is an RC whose installer evidence you may not want to keep.
3. **Freeze a commit and tag it.** `5980fa00` is green across all four certification jobs right now.
   Tag an RC (`v0.1.0-rc.1`), and the tag run mints `PRD-013`'s publish-smoke attachment,
   `PRD-015`'s dated drill artifact, `PRD-016`'s hosted run, `PRD-017`'s documentation-evidence job,
   and `PRD-014`'s first genuinely signed lifecycle receipts, all on one commit.
4. **Activate the required release check** (`PRD-016`, repository administrator).
5. **Sign off ADR-019 and ADR-020 and the support matrix** (`PRD-000`, core team). Note that Tier 1
   below feeds this directly: the tenancy posture is part of the envelope `PRD-000` must declare.

### State the limits precisely

An RC tag does not make the program production-ready. It produces the same-commit artifacts that
five P0 rows are explicitly waiting on, and it leaves three things untouched: the human approvals
(ADR sign-off, required-check activation, operator drill review), the W10 depth slate, and operator
acceptance of the bounded roadmap rows. It is also reversible — an RC that fails tells you which row
is not actually ready, which is information the program does not currently have for any of them.

## Tier 1 — `W9-GOV-008` has one bounded remainder, and its record overstates the rest

Two of three exit criteria are further along than the row says. Verified against current source:

- **Criterion one is complete.** `UnguardedMutationBaseline` is an **empty set**
  (`tests/Meridian.Tests/Integration/EndpointTests/EndpointAuthorizationCoverageTests.cs:96-98`),
  and the pre-binding `MutationAuthorizationGuardMiddleware` enforces the declaration at runtime
  rather than only in CI. The row's `current_summary` still carries the sentence "Remaining before
  acceptance - burn down the residual 112-route baseline (each fix shrinks it)". **That is false.**
  A later passage in the same summary records the burn-down correctly, so the row contradicts
  itself; the stale clause should go.
- **Criterion three is substantially complete, including the file-backed path that was the named
  correction.** `FileAccountingConfigurationStore.AppendAsync` now verifies the retained chain
  before appending, extends a tamper-evident chain, serializes cross-process, and exposes
  verification — with the criterion cited in the source comments
  (`src/Meridian.Ui.Shared/Services/AccountingConfigurationStores.cs:243-267,391`). The pre-chain
  history is reported as pre-chain rather than dressed up as protected, which is the right call.

**Criterion two is the remainder, and it is now one migration run plus one posture decision.**

The backfill tooling that had to come first exists: migration `004_fund_structure_tenant_columns.sql`
and `FundStructureTenantBackfillPlanner`. What has not happened is the flip:

- `TenantScopeEnforcementOptions` defaults to `DeploymentBoundary`
  (`src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs:18,43`).
- It is selectable **only by environment variable** — `TenantScopeServiceRegistration.cs:16-18`
  reads `TenantScopeEnforcementOptions.EnvironmentVariable` and falls back to `DeploymentBoundary`.
  There is no configuration key and no support-matrix statement, so the safe posture is reachable
  but undeclared.
- Under that default, `TenantReadPredicate` keeps emitting the `IS NULL` disjunct
  (`TenantReadPredicate.cs:79-81,95-97`), so **unattributed legacy rows stay visible to every
  caller**, and `PostgresFundStructureService` likewise keeps unattributed nodes visible
  (`PostgresFundStructureService.Tenancy.cs:182,229,264`).

Criterion two is categorical — "requests without resolvable tenant scope are rejected rather than
defaulted" — and the default posture defaults them. The work, in order: run the backfill against a
representative store, promote `FailClosed` to the supported default with a real configuration key
rather than an env var alone, add the rejection regressions the criterion asks for, and record the
posture in the support matrix. **Sequence this with Tier 0**, because `PRD-000` has to declare a
tenancy posture and this is that posture.

Row hygiene while you are there: `last_reviewed: 2026-08-29` is 29 days stale, and the remainder
text lists blockers that source has since closed.

## Tier 2 — `W9-INGEST-009` needs one PostgreSQL round trip, and it is the same gap that held `W9-CORPACT-011`

This is the highest-value *engineering* item on the list.

All four exit criteria now have source and evidence, reviewed 2026-09-25. The blocker that made this
row implementation work rather than verification — the internal ledger-transaction population that
"returns an empty ledger-transaction population by design" — is closed:
`LedgerJournalInternalTransactionSource` projects posted journals into custodian-visible
transactions, is registered in both compositions
(`CommandServiceRegistration.cs`, `WorkstationServiceCollectionExtensions.cs`), and is fail-closed
by contract. Split matching and group membership survive to the durable artifact
(`StatementRunMatcher.cs:91,103,148`), and the legacy artifact reader preserves hashes
(`StatementRunWorkflowService.cs:230-232`).

The row states its own boundary plainly: "This remains shared-service/file-store evidence with a
controlled population boundary and does not close operator acceptance."

**Measured: no PostgreSQL-backed test anywhere touches statement ingestion or reconciliation.**
Sixteen files use `[LedgerDatabaseFact]`; every one is ledger, asset-operations,
corporate-action, operations-continuity, or storage:

```
$ grep -rl "LedgerDatabaseFact" tests/ | grep -i "statement\|reconcil\|ingest"
(no matches)
```

`W9-CORPACT-011` sat blocked on exactly this shape of gap until `CorporateActionAccountingPostgresRoundTripTests`
closed it on 2026-09-22 — and that round trip **found two defects** in the shared spine posting path
that the file-store evidence had not surfaced. There is no reason to expect the statement path to be
different.

So: add a `StatementReconciliationPostgresRoundTripTests` — camt.053 and BAI2 import → the
journal-sourced ledger population → deterministic split match → casework projection, over the real
Postgres stores. It joins the certification lane automatically now that certification runs on every
push. Expect it to find something; that is the point. On the evidence it produces, the row is a
candidate to move to `ready_for_acceptance`.

## Tier 3 — The queue halved, and 8 of the 11 remaining branches are mechanical

Probe (reproducible): `git merge-tree --write-tree origin/main origin/codex/<branch>`, classifying
each conflicting path as generated/built or hand-written.

| PR | Branch | Conflicts | Hand-written | Disposition |
| --- | --- | --- | --- | --- |
| #2896 | `replay-parameter-fail-closed` | 112 | 0 | mechanical |
| #2920 | `p0-cash-ladder-currency-evidence` | 4 | 0 | mechanical |
| #2928 | `p0-csv-evidence` | 2 | 0 | mechanical |
| #2953 | `manual-journal-audit-recovery` | 52 | 0 | mechanical |
| #2998 | `acct-checklist-06-gl-providers` | 2 | 0 | mechanical |
| #2999 | `flush-after-rejected-batches` | 2 | 0 | mechanical |
| #3004 | `reconciliation-lineage-2636` | 3 | 0 | mechanical (its one "source" path is `docs/roadmap/generated/MANIFEST.json`) |
| #2931 | `p0-scoped-credential-ownership` | 7 | 1 | mechanical — the sole hand-written conflict is `docs/product/implementation-todo-list.md`, which resolves to `main` |
| #2897 | `portfolio-corporate-action-snapshots` | 121 | 1 | one file: `MultiSymbolMergeEnumerator.cs` |
| #2929 | `p0-ofx-account-scope` | 172 | 2 | the two shared operations-continuity fixtures |
| #2930 | `p0-statement-durable-commit` | 172 | 2 | same two fixtures |
| #2826 | `first-trusted-close` | 4 | 4 | genuine: the operations-continuity screen plus `LedgerJournalInternalTransactionSource.cs` |
| #2789 | `backtesting-quantscript-milestone-1` | 129 | 9 | still correctly self-labelled `[DRAFT — RE-CUT REQUIRED]` |

The shared-fixture finding from 2026-09-23 still holds, just smaller: resolving
`FinancialOperationsCommandCenterReadService.PublicationTests.cs` and
`OperationsContinuityWorkflowServiceTests.cs` once on `main` converts #2929 and #2930 into the
mechanical case.

Note #2826 specifically: it conflicts on `LedgerJournalInternalTransactionSource.cs`, the file Tier 2
depends on. Whatever it was doing to the internal transaction source has since been done on `main`.
Read it before merging it, not after.

**The merge policy is still unimplemented.** `.gitattributes` defines no merge driver for the
generated trees. In the last 30 days the tracked workstation asset tree — 118 files, 117 of them
content-hashed bundles — absorbed 1,820 file-touch events across 1,452 distinct paths, because Vite
renames on every build. That is the mechanism producing 90%+ of every conflict in the table above,
and it will keep producing them. The fix remains a `merge=ours`-style driver (or a documented
regenerate-on-merge command) for `src/Meridian.Ui/wwwroot/workstation/**`, `docs/generated/**`,
`docs/status/**`, `docs/diagrams/**`, `docs/roadmap/generated/**`, `database/manifest/**`, and
`src/*/README.md`. Neither option changes what is tracked, so neither disturbs `PRD-013` or
`PRD-018`.

Also close **#2983**, the 2026-09-22 determination. It is now three documents stale and its findings
landed elsewhere.

## Tier 4 — One operator decision is outstanding

`W9-SAFETY-007` was taken on 2026-09-23 (Tier 2 of the last document, now closed). One remains:

- **`W9-CORPACT-011`** has been at `ready_for_acceptance` since 2026-09-22, five days.
  `DEC-W9-ACCEPTANCE-002`, the decision that reopened it, says acceptance "may be reconsidered only
  after the approval and posting lane is reachable and the criterion is implemented and evidenced."
  It is, on PostgreSQL, and the round trip is in the certification lane. The reconsideration is due.
  Costs no engineering. Declining is a result too.

## Tier 5 — W10: two rows await an operator session, one has an undefined criterion

- **`W10-MARK-001`** and **`W10-SEAM-001`** are both implementation-complete with the stale
  certification sentence now removed from their records. Each has exactly one remaining blocker and
  it is **not engineering**: live WPF rendering and population-wide preview for `MARK`, live
  operator certification for `SEAM`. `SEAM`'s recorded sequencing handshake with
  `W8-WPF-PARITY-001` still makes it the one to certify first, because closing it removes a
  dependency from the desktop lane.
- **`W10-LOT-002`** is the slate's only `critical` row and the tracker's stated continuation. Its
  four named remainders are not equally shaped, and one needs scoping before it is worked:
  - *Amortization* is partly present — `FixedIncomeAmortizationProjector`,
    `FixedIncomeAmortizationProjection`, and `LedgerTaxLotBasisAdjuster` exist in
    `src/Meridian.Ledger`. The work is convergence onto the canonical lot contract, not greenfield.
  - *Corporate-action successors* and *advance refunding* appear in the corporate-action type and
    payload catalogs (`CorporateActionTypeDescriptorCatalog`, `CorporateActionEventTypes`,
    `ClearwaterCorporateActionRuleProfileV1`) but not yet as append-only predecessor/successor lot
    mutations. That is the real engineering remainder.
  - *"Shadow-operation acceptance"* has **no source presence at all**: `grep -rl "ShadowOperation"`
    over `src/` and `tests/` returns nothing. A criterion with no implementation and no definition
    anywhere in the tree cannot be burned down. **Define it or drop it** before counting it as a
    blocker on a `critical` row.

## What not to work on

- **New product surface.** The `scope_gate` in `program-state.yml` is explicit, and seven rows are
  in progress or awaiting acceptance across three waves.
- **The other eight planned W10 rows.** `W10-RECON-001` through `W10-CONSOL-001` and `W10-DEBT-001`
  stay planned. Nine lanes open at once is why none of them closes.
- **Re-cutting the large stranded branches.** Tier 3 measures it again and the answer is unchanged:
  only #2789.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` both require one tracked canonical
  tree; fix the merge, not the tracking.
- **Re-litigating accepted W9 rows.** Six are accepted under recorded decisions and one is done.
- **Re-proving the certification lane.** It runs per push and is green on `5980fa00`. The open
  question is the *frozen commit*, which is Tier 0.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| P0 | Provide `MDC_SIGNING_CERT_PFX_BASE64` in the protected `desktop-release-signing` environment | Hard prerequisite for everything else in Tier 0; not engineering work |
| P0 | Decide the ~1 GB consumer installer question | It ships inside the RC; decide before the tag, not after |
| P0 | Freeze a commit and cut `v0.1.0-rc.1` | One tag run mints the same-commit evidence five P0 rows are explicitly waiting on; no `v*` tag has ever existed |
| P0 | Backfill tenancy, make `FailClosed` the supported default with a real config key, add the rejection regressions | The only remaining `W9-GOV-008` criterion, and `PRD-000` must declare this posture anyway |
| P0 | Add `StatementReconciliationPostgresRoundTripTests` | Nothing proves the statement/reconciliation path on PostgreSQL; the identical gap held `W9-CORPACT-011` and its round trip found two real defects |
| P1 | Take or decline `W9-CORPACT-011` (5 days) | Zero engineering; clears the last W9 acceptance lane |
| P1 | Drain the eight mechanically-mergeable branches; resolve the two shared fixtures once on `main` | No product decision, implemented P0/P1 value, and it converts #2929/#2930 too |
| P1 | Add the `.gitattributes` merge driver for the generated trees | 1,820 touch events in 30 days on 118 tracked asset files; recurring by construction and still unimplemented |
| P2 | Schedule `W10-SEAM-001` live certification, then `W10-MARK-001` | Both are one operator session from closing, and `SEAM` unblocks the desktop lane |
| P2 | Continue `W10-LOT-002` on successors and advance refunding; **scope or drop "shadow-operation acceptance"** | The only `critical` row, carrying one criterion that exists nowhere in source |
| P3 | Correct the `W9-GOV-008` row (self-contradicting remainder, 29 days stale); close #2983 | Both currently misdescribe the program |
