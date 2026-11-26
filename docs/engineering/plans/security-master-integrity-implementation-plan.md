# Security Master Integrity and Traceability Implementation Plan

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-11-18
**Scope:** Engineering

**Source evidence**
- The 2026-11-18 scheduled Security Master review at `0adea571` (read-only, no tests run).
- Open PR #3603 (`codex/security-pack-foundation`), compared at `ecb9eed5` against its base `c0329a19`.

**Related**
- [Security Master review remediation](security-master-review-remediation-plan.md). Its slices and owner decisions remain authoritative.
- [Security domain](../../domain/security.md).
- [Roadmap registry](../../roadmap/README.md), which owns delivery status.

This plan turns the review's top priorities into reviewable slices:
- silent leg economics;
- identifier integrity;
- routing for every revision;
- consumption lineage;
- workbench parity;
- an index-fixing service;
- valid-time terms.

It is a working input, not a status source. It assigns no roadmap rank, release readiness or target date. Each slice still needs a fresh source comparison, focused regressions, the repository's independent schema/storage review for any migration, and the hosted `Meridian CI / quality-gate`.

## Ground rules

- **Work mode.** Phases 1, 2 and 5 are *Standard*. Phases 3, 4, 6 and 7 are *Deep Review*: they touch storage, ledger or event semantics. Record the mode and first proof command in each PR (`docs/ai/work-modes.md`).
- **Risk tier.** Every slice that changes calculations, identity or storage is Level 2 (financial/architectural) under `docs/ai/solo-developer-workflow.md`.
- **Refuse, don't guess.**
  - New behaviour blocks with a typed reason rather than defaulting.
  - Frozen receipt and replay versions keep their historical arithmetic.
  - New behaviour gets a new version stamp.
- **No stored-history rewrites.** `security_events`, governance events and retained receipts stay as written (migrations 059, 040/041). Legacy shapes are read through labelled compatibility readers.
- **Migration numbering.** PR #3603 claims Security Master `069` and Ledger `073`. This plan uses `070+` and renumbers at rebase if main moves.
- **Synthetic fixtures only.** No live accounts or production activation is implied by any slice.

## Sequencing and dependencies

```text
Phase 0 (preconditions, read-only diagnostics)
   │
   ├─► Phase 1  Leg economics guards ───────────────┐   (rebase onto #3603)
   ├─► Phase 2  Identifier integrity (2a→2d)        │
   ├─► Phase 5a Browser workbench parity            │   (rebase onto #3603)
   │                                                ▼
   └─► Phase 3  Revision outbox ──► Phase 4  Consumption lineage
                     │
                     ├─► Phase 6  Index-fixing service   [decision D-FIX]
                     └─► Phase 7  Valid-time terms        [decision D-VT, ADR]
Phase 5b Approve/publish permissions                      [decision D-PERM]
```

Phases 1, 2 and 5a can run in parallel because their file sets are disjoint (see the manifest below). Phase 3 must land before Phases 4, 6 and 7, because those phases rely on corrections being routed.

## Decisions required

| ID | Question | Blocks | Recommendation |
|---|---|---|---|
| D-XCCY | Refuse cross-currency swaps on the legacy projection path, or support them with per-currency schedules and multi-curve discounting? | Phase 1 scope only (refusal can ship without this decision) | Refuse now. Support belongs on the contractual engine. |
| D-DUP | For globally unique identifier kinds, should a cross-security duplicate be refused at write, or recorded atomically as a conflict? | Phase 2b | Record atomically by default. Refuse only for ISIN/CUSIP/FIGI once the Phase 0 collision count is known. |
| D-LIN | Which consumers must stamp the revision they used? | Phase 4 scope | Cash-flow receipts, ledger postings, certified report snapshots, lot openings. |
| D-PERM | Separate approve/publish permissions; must the publisher differ from the maker under Institutional? | Phase 5b | Yes to both. |
| D-FIX | Does the rate-index catalogue live in Security Master reference data or in a market-data lane? | Phase 6 | Market data, with Security Master referencing index ids. |
| D-VT | Are retroactive corrections of contractual terms in scope? Which downstream results re-run automatically, and which are only proposed? | Phase 7b+ | Corrections in scope; downstream re-runs proposed, never auto-applied. |
| D3 (existing) | Canonical rate units and ranges | Not blocking; Phase 1 reads declared units only | See remediation plan slice 4 |

## Parallel task manifest (Phases 1, 2, 5a)

| Lane | Initial file set (ownership) | Validation owner / first proof |
|---|---|---|
| Leg economics | `src/Meridian.Contracts/SecurityMaster/SecurityMasterCashFlow.cs`, `StructuredCashFlowTermsResolver.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService*.cs`, `Valuation/DiscountedCashFlowValuationModel.cs` | `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~SecurityMasterCashFlow"` |
| Identifier integrity | `src/Meridian.FSharp/Domain/SecurityMasterCommands.fs`, `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore*.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterService*.cs`, `*Conflict*.cs`, `SecurityMasterQueryService.cs`, `CorporateActions/SecurityMasterTickerChangeService.cs` | `dotnet test tests/Meridian.FSharp.Tests` and then `--filter "FullyQualifiedName~SecurityIdentifier"` |
| Workbench parity | `src/Meridian.Ui/dashboard/src/components/meridian/security-passport-editor*.ts(x)`, `src/Meridian.Ui/dashboard/src/lib/api/security-master-workbench.api.ts` | `npm --prefix src/Meridian.Ui/dashboard run test` |

`SecurityMasterService.cs` is touched by both the leg-economics lane (only through the cash-flow service) and the identifier lane. The identifier lane owns it. The leg lane must not edit it.

---

## Phase 0 — Preconditions and sizing (read-only)

**Goal:** size the later phases with evidence before writing code, and settle the merge order with #3603.

**Steps**
- **0.1 Merge order.** Decide whether #3603 merges first. It edits `SecurityMasterCashFlowService.cs` (reviewed-accrual branch), `security-passport-editor.tsx` (impact preview), `SecurityMasterService.cs` and the revision store. Phases 1 and 5a should rebase onto it rather than conflict with it.
- **0.2 Collision count (read-only SQL against a synthetic or staging copy).**
  - Active cross-security overlaps on `security_identifiers(identifier_kind, normalized_identifier_value)` for ISIN, CUSIP, FIGI and SEDOL, where validity windows overlap.
  - This sizes D-DUP and the Phase 2b pre-flight.
- **0.3 Re-import behaviour.** Re-import a previously imported synthetic CSV through `SecurityMasterImportService` and record how many rows come back failed rather than skipped. This confirms the review's inference.
- **0.4 Ticker-change reproduction.** Add an *expected-failure* characterization test only if the team wants one before Phase 2a. Otherwise Phase 2a's test covers it.

**Checklist**
- [ ] #3603 merge order recorded in the Phase 1 and 5a PRs
- [ ] Collision-count query and result attached to the D-DUP decision
- [ ] Re-import result recorded (supports the deferred bulk-onboarding work)

---

## Phase 1 — Leg economics guards (no decision needed; D-XCCY only widens scope)

**Problem (verified at `0adea571`)**
- `StructuredCashFlowLeg` (`SecurityMasterCashFlow.cs:224-237`) has no currency.
- `StructuredCashFlowTermsResolver.ReadLeg` drops each leg's currency and its own `couponRateUnit`.
- `SecurityMasterCashFlowService.BuildNetSchedule` (`:736-766`) nets receive minus pay across legs regardless of currency.
- DCF then discounts the result on one curve in the security currency.
- #3603's `SecurityVanillaSwapProfile` refuses cross-currency only on the contractual path. The legacy path is unchanged.

### Slice 1a — Currency-aware legs and refusal of mixed-currency netting

**Changes**
1. Add `string? Currency` and `SecurityRecordRateUnit? RateUnit` to `StructuredCashFlowLeg`. Both are optional for source compatibility; nothing infers either value.
2. In `StructuredCashFlowTermsResolver.ReadLeg`, read `currency` and `couponRateUnit` from each leg. Read only declared values, never magnitude.
3. Add receipt model `bond-cash-flow-context-v6` beside `ContextCalculationVersion` in `SecurityMasterCashFlowService.Context.cs`. Only v6 applies the new guards; v1–v5 (and #3603's reviewed-accrual version) replay unchanged.
4. Under v6, projection of a multi-leg security returns a blocked result when:
   - any leg's currency is missing → reason `LegCurrencyUndeclared`;
   - leg currencies differ from each other or from the security currency → reason `MixedCurrencyLegs`.
5. `LegRateFraction` prefers the leg's declared unit, then the record unit, matching the catalog comment at `SecurityRateFieldUnits.cs:107`.
6. `DiscountedCashFlowValuationModel` refuses a projection whose legs carry more than one currency (defence in depth).

**Tests (new, synthetic)**
- A USD-fixed/EUR-float swap: blocked `MixedCurrencyLegs`; DCF returns no value plus a diagnostic.
- A USD/USD IRS: v5 receipts replay byte-identical. A new v6 run equals v5 when all legs declare USD.
- Leg unit parity: `Percent` 4.25 and `Fraction` 0.0425 on a leg produce identical interest. A leg with no unit and a record unit falls back to the record unit. Neither declared blocks as today.
- A frozen v1 receipt fixture replays unchanged.

**Acceptance:** the four tests above pass, and the existing `SecurityMasterCashFlow*` and `DiscountedCashFlow*` suites pass unchanged.

**Effort:** about 2–3 days. Contracts change additively; no migration.

**Checklist**
- [ ] Rebased onto #3603 if it has merged
- [ ] Leg currency and unit read in `ReadLeg`
- [ ] v6 model version and guards
- [ ] DCF refusal
- [ ] Tests above, with fixtures under `tests/fixtures/security-master/`
- [ ] `docs/domain/security.md` updated: legacy multi-leg projection refuses mixed currency

### Slice 1b — Turn the remaining silent defaults into typed blocks (live paths only)

**Each item becomes a v6 block. Frozen versions are unchanged.**

| Silent behaviour today | Location | v6 behaviour |
|---|---|---|
| Unknown or missing day count becomes Act/365 | `SecurityMasterCashFlowService.cs:~430`, `FactorAccrual.cs:117-118` via `DayCountConventions.Fraction` | Block `DayCountUndeclared` (SCH-DEF-004) |
| Missing current-period reset reuses the previous fixing | `FixingOn` in `SecurityMasterCashFlow.cs:272-276`; `ResolveLegAnnualRate` | Block `HistoricalFixingMissing` when the period's reset date has passed |
| `CurrentIndexRate` used without a reset-date check | `ResolveLegAnnualRate` `:717-720` | Allowed only with an explicit `currentIndexRateAsOf` covering the period; otherwise block |
| Latent `?? 0m` rate | `:720` | Throw an internal invariant failure (unreachable in practice) |

The direct-lending zero rate (SCH-DEF-005, `PostgresDirectLendingCommandService.cs:1543-1545`) is a separate slice, **1c**, in the direct-lending lane. A floating loan with no reset must persist a blocked run, not a zero-interest run.

**Tests:** move the SCH-DEF-004 and SCH-DEF-005 fixtures in `tests/fixtures/schedules/KNOWN-DEFECTS.md` from "documented defect" to "refused", and update the registry entry.

**Effort:** about 2–3 days for 1b and 1–2 days for 1c.

**Checklist**
- [ ] 1b blocks plus fixtures
- [ ] 1c direct-lending blocked run
- [ ] KNOWN-DEFECTS registry updated

---

## Phase 2 — Identifier integrity

### Slice 2a — Primary identifier rotation (first, smallest, demonstrated defect)

**Problem**
- `SecurityMasterTickerChangeService` expires the old ticker at `EffectiveFrom` and copies `IsPrimary` to the new ticker.
- `validateAmend` refuses any primary identifier whose `ValidTo = EffectiveFrom` (`primary_identifier_expiry_forbidden`, `SecurityMasterCommands.fs:762-778`).
- No test asserts that refusal, and the service's tests mock the command path.

**Change (preferred: the smallest one)**
- Relax `validateAmend`. A primary identifier may expire at `EffectiveFrom` when the same command adds exactly one primary of the **same kind** with `ValidFrom = EffectiveFrom`.
- Keep `validateRecord`'s "exactly one active primary" invariant.
- *Alternative considered:* a separate `RotatePrimaryIdentifier` command. It is clearer intent but means more surface. Revisit only if other rotation flows appear.

**Also check**
- The unique index `ux_securities_normalized_primary_identifier` (migration 032) is updated with the projection in the same transaction (`PostgresSecurityMasterStore` upsert).
- `SecurityMasterAmortizationLedgerBridge` keys accounts by ticker (`LedgerAccounts.Securities(normalizedTicker, …)`). Confirm or refute that a rename splits account history. If it does, open a follow-up to key by `SecurityId` with a mapping for existing accounts.

**Tests**
- F# (`tests/Meridian.FSharp.Tests`): rotation is accepted; expiring a primary without a same-kind replacement is still refused; two new primaries are refused.
- PostgreSQL integration: ABC→XYZ on a CSV-imported (primary) ticker succeeds, keeps the same `security_id`, and:
  - as-of before the change resolves ABC;
  - as-of after resolves XYZ;
  - XYZ resolves to nothing before the change.

**Effort:** about 1–2 days.

**Checklist**
- [ ] Domain rule and F# tests
- [ ] PostgreSQL ticker-change test using the real command path, not a mock
- [ ] Ledger-account keying checked and the result recorded

### Slice 2b — Ambiguity is a result, not a silent choice

**Changes**
1. `PostgresSecurityMasterStore.ResolveSecurityIdAsync` (`:805-884`) stops using `limit 1` as the decision. It fetches up to two candidates per stage and returns a typed `SecurityResolution { Resolved(id) | Ambiguous(ids) | NotFound }`.
   - Existing `Guid?` callers get a compatibility overload that returns `null` on `Ambiguous` and logs structured fields. This matches the recorded-as-of path (`SecurityMasterQueryService.cs:85-182`).
2. Move cross-security duplicate detection for ISIN, CUSIP, FIGI and SEDOL into the canonical write transaction (`SecurityMasterCanonicalWriteScope`).
   - Per D-DUP, either refuse, or insert the conflict row in the same transaction.
   - Remove the swallow-after-commit path (`SecurityMasterService.cs:586-598`, `Creation.cs:71`) for these kinds.
3. A pre-flight report (reusing the Phase 0.2 query), as migration 032 did, before any refusal mode is enabled.

**Tests**
- Two securities share an active ISIN: live resolution returns `Ambiguous`, and the compatibility overload returns null.
- Create with a colliding ISIN: either refused, or a conflict row is committed atomically.
- A forced failure in conflict insertion rolls back the create.

**Effort:** about 3–4 days.

**Checklist**
- [ ] Typed resolution plus compatibility overload
- [ ] In-transaction duplicate handling per D-DUP
- [ ] Pre-flight report
- [ ] Consumers audited (`git grep ResolveSecurityIdAsync`) for null handling

### Slice 2c — Conflict resolution produces the fix and is audited

**Changes**
1. When the chosen winner is not the persisted value, `PostgresSecurityMasterConflictService.ResolveAsync` creates a governed workbench draft bound to the conflict id. It does not throw the "amend then retry" error. The conflict moves to `PendingAmendment` and resolves when the draft publishes.
2. Migration `070_security_master_conflict_resolution_evidence.sql`:
   - adds `policy_winner`, `chosen_winner`, `deviation_acknowledged` and `resolution_rationale` to `security_master_conflicts`;
   - adds the `Conflicts` stream to `security_governance_events` (pattern of 051/053/055).
3. Each state transition appends a governance event in the same transaction.

**Tests:** resolving to the challenger creates a pending draft; publishing that draft resolves the conflict; deviation from policy is persisted; governance events are append-only.

**Effort:** about 3–5 days, plus migration review.

**Checklist**
- [ ] Draft-on-resolve
- [ ] Migration 070 with independent schema review
- [ ] Governance stream
- [ ] `ISecurityMasterConflictAuthorityPolicy` doc corrected (it omits the precedence step)

### Slice 2d — Use the two-axis identifier history for recorded-as-of lookups

**Problem:** `SecurityMasterQueryService` (`:107-140`) replays the whole event log for each recorded-as-of lookup. `ISecurityIdentifierHistoryStore.GetIdentifiersRecordedAsOfAsync` (migration 062) is registered but has no production caller.

**Change**
- Answer recorded-as-of identifier lookups from `security_identifier_revisions`.
- Fall back to the event replay only before `history_available_from`, and label that result.
- Record the human actor on identifier revisions instead of the fixed `"security-master.projection-replacement"`.

**Tests:** parity between the event replay and the revision table on a synthetic history, for times after the cutoff.

**Effort:** about 2–3 days.

**Checklist**
- [ ] Revision-table read path plus labelled fallback
- [ ] Actor propagation
- [ ] Parity test

---

## Phase 3 — One routed change record for every canonical revision

**Problem:**
- `ISecurityMasterRevisionPublishedHandler`s and period-aware restatement run only for governed workbench publishes.
- Direct amendments, system ingest (EDGAR, backfill), ticker changes, imports and deactivations route nowhere except the projection notification.
- #3603 adds a pre-approval impact receipt for workbench revisions (`BindImpactPreviewAsync`). It does not cover the other write origins.

**Changes**
1. Migration `071_security_revision_outbox.sql` creates `security_revision_outbox` with these columns:
   - `security_id`, `stream_version`;
   - `change_origin` (`Workbench | SystemIngest | DirectAmend | Import | CorporateAction | Deactivation`);
   - `changed_paths jsonb`;
   - `recorded_at`, `dispatched_at`, `attempts`, `last_error`.
   - Primary key `(security_id, stream_version)`.
2. Write the outbox row inside `SecurityMasterCanonicalWriteScope` for every event append, so it commits atomically with the event. `changed_paths` comes from diffing the prior and next definition.
3. A hosted `SecurityRevisionOutboxDispatcher`:
   - claims rows with `FOR UPDATE SKIP LOCKED`;
   - runs the existing handler chain (`WorkstationServiceCollectionExtensions.cs:753-764`);
   - also runs a new `RestatementRoutingHandler` that calls `IPeriodAwareRestatementResolver` for non-workbench origins;
   - marks rows dispatched; retries with backoff; dead-letters after N attempts.
4. Handlers become idempotent per `(security_id, stream_version)`. Workbench publishing keeps its synchronous path but writes the outbox row as well, and the dispatcher skips handlers already completed for that version.

**Tests**
- A system-ingest coupon correction on a bond with a lot in a soft-closed period produces the same restatement proposal as a workbench publish.
- If the process stops after commit and before dispatch, the restarted dispatcher delivers exactly once.
- Two dispatchers do not double-deliver (PostgreSQL).
- Workbench publish is not routed twice.

**Effort:** about 1–1.5 weeks.

**Checklist**
- [ ] Migration 071 with independent schema review
- [ ] Outbox write in the canonical scope
- [ ] Dispatcher plus dead-letter visibility on the operational readiness surface
- [ ] Idempotency audit of the existing handlers
- [ ] `docs/domain/security.md` "Governed edit concurrency" section extended with outbox semantics

---

## Phase 4 — Revision consumption lineage (depends on Phase 3 and D-LIN)

**Problem:**
- Only `asset_accounting_events.expected_security_version` and certified reporting snapshots retain the version they used.
- Cash-flow receipts embed it in files only.
- No reverse query exists.
- `IAffectedLedgerBookResolver` therefore returns every fund book.

**Changes**
1. Migration `072_security_revision_consumers.sql` creates an append-only table `security_revision_consumers` with columns `security_id`, `stream_version`, `consumer_kind`, `consumer_ref`, `fund_id`, `book_id` and `recorded_at`. Unique on `(consumer_kind, consumer_ref, security_id)`.
2. Stamp writers, one sub-slice each:
   - **4a** asset accounting events (existing version) and certified report snapshots (existing version);
   - **4b** cash-flow receipts (`FileSecurityCashFlowReceiptStore` on retain);
   - **4c** lot opening and factor paydown;
   - **4d** valuation marks.
3. `GET /api/security-master/{id}/revisions/{version}/consumers`, plus a browser panel on the history view.
4. A narrowing resolver: when consumer rows exist for the affected version, the restatement candidate resolver uses their books. Otherwise it falls back to fund-wide, labelled.
5. Revisions before the cutoff report "lineage unavailable before {cutoff}" (the alias `history_available_from` pattern).

**Acceptance:** for a synthetic bond corrected at version 7, the consumers endpoint lists:
- the cash-flow receipt;
- the posted accrual journal;
- the report snapshot produced from version 6;
- and the restatement proposal names only the books that hold the lot.

**Effort:** about 1 week for 4a plus the endpoint, then about 1–2 days per further consumer.

**Checklist**
- [ ] Migration 072 with independent schema review
- [ ] 4a
- [ ] 4b
- [ ] 4c
- [ ] 4d
- [ ] Endpoint and browser panel
- [ ] Narrowing resolver with labelled fallback

---

## Phase 5 — Operator workbench parity and permissions

### Slice 5a — Browser parity (rebase onto #3603's passport-editor changes)

**Gaps in `security-passport-editor.tsx` / `.view-model.ts`**
1. No discard action. Add a client for `POST /api/security-master/{id}/workbench/discard`.
2. `handlePublish` ignores `restatementRequired`, `restatementCandidates` and `canonicalMerge`. Show them, matching WPF (`SecurityPassportEditorViewModel.cs:489-490`).
3. `StaleFieldProposalException` (409) shows the generic revision-state message. Map it to "This field changed after the draft was created. Discard and re-stage."
4. Free-text workflow GUID and reviewer. Replace with pickers backed by the existing continuity and workflow read models.

**Tests:** dashboard unit tests for each mapping; one Playwright workflow test (stage → submit → approve → publish with a restatement banner).

**Effort:** about 3–4 days.

**Checklist**
- [ ] Discard
- [ ] Restatement and merge outcome display
- [ ] Stale-proposal message
- [ ] Workflow and reviewer pickers
- [ ] WPF parity note in `docs/engineering/plans/wpf-web-ui-alignment-plan.md` (WPF also lacks discard)

### Slice 5b — Separate approve and publish rights (decision D-PERM)

**Changes**
- Add `ApproveSecurityMasterChange` to `UserPermission`, and grant it in `RolePermissions` to the reviewer role(s) the owner names.
- The approve route requires the new permission.
- Under Institutional, publish refuses when the publisher is the revision's maker.

**Tests:** permission matrix tests; same-actor publish refused under Institutional and allowed under Standard.

**Effort:** about 1–2 days once decided. Governance change: human review required.

**Checklist**
- [ ] D-PERM recorded
- [ ] Permission, role grants and route guard
- [ ] Publisher ≠ maker rule
- [ ] Operator doc `security-master-governed-edit-rollout.md` updated

---

## Phase 6 — Index-fixing observation service (decision D-FIX; after Phase 3)

**Problem:** fixings live in five places:
- terms `resetSchedule` / `currentIndexRate`;
- `FileBondObservedFixingStore`, which the cash-flow service never reads;
- OTC `RateReset` lifecycle events, with no consumer;
- the direct-lending index-fixing store;
- the intercompany rate-fixing store.

**Changes**
1. Migration `073_index_fixings.sql` (or in the market-data schema per D-FIX):
   - `index_fixings(index_id, fixing_date, value, unit, source, published_at, recorded_at, supersedes_fixing_id)`, append-only;
   - corrections are new rows;
   - an index catalogue table.
2. `IIndexFixingProvider` with `GetFixing(indexId, date, knownAt)` returning the value, its declared unit and receipt evidence.
3. Consumers, one slice each:
   - **6a** declared bond leg and the v6 legacy leg path;
   - **6b** direct lending, through a read-through adapter first, then a data migration;
   - **6c** intercompany;
   - **6d** OTC `RateReset` events, published as fixings.
4. A fixing correction writes a Phase 3 outbox-equivalent event for each security referencing the index. Affected projections then route without amending terms.

**Acceptance**
- A missing current-period SOFR fixing blocks on every live path.
- A corrected fixing produces new receipts that differ only in the affected periods.
- A −0.10% EURIBOR fixing under a declared signed policy produces negative index interest, floored only where the terms declare a floor.

**Effort:** about 2–3 weeks.

**Checklist**
- [ ] D-FIX recorded
- [ ] Migration with schema review
- [ ] Provider
- [ ] 6a
- [ ] 6b
- [ ] 6c
- [ ] 6d
- [ ] Correction routing

---

## Phase 7 — Valid-time economic terms (decision D-VT; ADR before code beyond 7a)

**Problem:**
- F# `amend` keeps `current.EffectiveFrom` (`SecurityMasterCommands.fs:931-939`).
- The request's effective date only closes identifiers and is retained as `Provenance.AsOf`.
- Terms have recording time but no valid time, so a retroactive correction looks the same as a prospective change.

### Slice 7a — Record the fact now (no ADR needed)

**Changes**
- Persist the amendment's `EffectiveFrom` and an explicit `changeKind` (`Change` or `Correction`, with an optional `correctsVersion`) in event metadata. This is additive: payload readers ignore unknown metadata, and payload version (039) is unchanged.
- Show both in the history view.
- Include `changeKind` in the Phase 3 outbox row.

**Tests:** metadata round-trip; old events read as "valid time not recorded".

**Effort:** about 2–3 days.

### Slice 7b+ — Bitemporal fold (ADR-028 candidate)

**ADR scope**
- Event payload versioning for `valid_from`.
- Fold order (valid time within recorded time).
- Readable-before-writable rollout (ADR-024 pattern).
- As-of API changes in `SecurityMasterAggregateRebuilder`.
- How corrections produce restatement proposals through Phase 3/4.

**Acceptance (target)**
- A coupon is corrected from 5.00% to 5.25%, effective from issue and recorded three months later.
- As-of (valid = issue + 1m, recorded = issue + 2m) returns 5.00%.
- As-of (valid = issue + 1m, recorded = now) returns 5.25%.
- The re-accrual proposal covers exactly the affected periods.

**Effort:** about 3–6 weeks after the ADR is accepted. Low confidence.

**Checklist**
- [ ] 7a metadata and history display
- [ ] D-VT recorded
- [ ] ADR drafted and accepted
- [ ] Implementation slices defined from the ADR

---

## Deferred (tracked, not scheduled here)

| Item | Why deferred |
|---|---|
| Converge on the contractual cash-flow engine per class, and let the ledger gate admit contractual output | Re-evaluate after #3603's IRS pilot, using per-class diff counts between the two engines |
| Single owner for loan contractual terms (Security Master vs direct lending) | Needs an ownership decision. Run a read-only drift report first |
| Issuer / obligor entity | No blocked consumer is demonstrated yet. Run an LEI coverage count first |
| Factor observations with automatic `FactorPaydown` proposals | Follows Phase 6's observation pattern, and needs a term-vs-observation ruling |
| Governed, idempotent bulk onboarding | Reuse #3603's repair-batch pattern after it merges. Phase 0.3 supplies sizing |
| Typed `CurrencyCode` and an audit of absent-as-false sentinels | Small; schedule opportunistically |
| Remove or wire dormant code (`SecurityMasterCostBasisAdjustmentService`, `FaceValueLotExtensions.ToLedgerTaxLot`/`ApplyFactor`) | Check W10-LOT-002's intended use first |
| Generate the F# asset registry from the C# catalog | Parity tests already catch drift; low return |

## Validation floor per phase

| Phase | Local proof | Hosted |
|---|---|---|
| 1 | Filtered `Meridian.Tests` (`SecurityMasterCashFlow`, `DiscountedCashFlow`, `StructuredCashFlow`); direct-lending tests for 1c | quality-gate |
| 2 | `Meridian.FSharp.Tests`; filtered `Meridian.Tests` (`SecurityIdentifier`, `SecurityMasterConflict`, `TickerChange`) with Docker PostgreSQL | quality-gate and integration-gate |
| 3–4, 6 | Storage and application suites with PostgreSQL; migration applied through `SecurityMasterMigrationRunner` | quality-gate, integration-gate, independent schema review |
| 5a | `npm --prefix src/Meridian.Ui/dashboard run test`, build, one Playwright workflow | quality-gate |
| 7a | Filtered event/history tests | quality-gate |

When local validation is unreliable, use the `Targeted Test` workflow (`mode=dotnet-filtered`) as described in `CLAUDE.md`.
