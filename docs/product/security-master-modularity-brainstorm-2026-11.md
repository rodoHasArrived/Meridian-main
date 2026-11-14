# Security Master Modularity and Operator Value — Brainstorm (2026-11)

**Status:** dated working design input — not a status source. Live status is the roadmap registry.
**Source evidence:** HEAD `3503ae000` (2026-11-14) and the Security Master review from the same day.
**Mode:** Architecture / Refactoring, plus Domain-Focused (Portfolio and valuation).

## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | Asset pack as the module boundary | L | ADMIN, ACCT | High | — |
| 2 | Instrument capability card ("what can Meridian do with this?") | M | OPS, ACCT, INV | High | — (better after #1) |
| 3 | Declarative accrual eligibility | L | ACCT | High | #1 (term roles already exist) |
| 4 | Rate-unit repair queue with batch governed drafts | M | OPS, ACCT | High | — |
| 5 | Edit impact preview before approval | M | ACCT, GOV | High | — |
| 6 | Reusable leg modules | L–XL | INV, ACCT | High | #1 |
| 7 | CustomAsset → candidate pack promotion | M | ADMIN, OPS | Med | #1 |
| 8 | "What we knew when" compare in the passport | S–M | GOV, ACCT | Med | — |

## 1. Asset pack as the module boundary (platform bet)

**Today.** Adding a first-class class touches 16 hand-maintained dispatch sites (see the
`KnownDispatchSites` list in `tests/Meridian.Tests/Architecture/SecurityAssetClassLiteralRatchetTests.cs`).
Tests catch omissions, but nothing gives a class one home. `SecurityAssetPackDescriptor`
(`src/Meridian.Contracts/SecurityMaster/SecurityAssetPackRegistry.cs`) already describes a pack:
schema, lifecycle coverage, valuation methods, accounting rules, admission policy and automation depth.
Today that descriptor is only metadata.

**Proposal.** Make the pack the unit of code ownership. Each class contributes one
`ISecurityAssetPackModule`, which supplies:
- its schema
- its cash-flow term roles
- its rate-unit table rows
- its validators
- its upcaster steps
- its codec binding rows
- its accounting template IDs
- its term-form descriptor

The registries become composed reads over the modules:
- `AssetClassValidatorRegistry`
- `SecurityRateFieldUnits`
- `SecurityTermsProjectionRegistry`
- `SecurityAssetSpecificTermsUpcasterChain`
- `SecurityAssetTermsCodecBindings`

The F# `SecurityKind` union and the generated codec stay. The union is the right closed type for
the calculation kernels. Only the C# fan-out moves.

**Before / after.** Today a new class means 16 file edits plus parity tests. After the change it
means one pack folder plus a union case, and the ratchet list shrinks toward the F# files and the
generated codec.

**Operator moment.** This is indirect: new classes arrive faster and with complete coverage. The
admin-facing benefit is a Settings → Asset packs page that lists each module with its tests.

**Tradeoffs.**
- Composition order matters for upcasters and spelling priority. Keep explicit ordinals.
- Migrate one registry at a time, behind equivalence tests like `SecurityDefinitionValidationEquivalenceTests`.

## 2. Instrument capability card

**Today.** Operators find gaps by running into them: Bond-only accrual, swap per-leg refusals,
principal-only redemptions, undeclared rate units. The facts already exist in pack lifecycle
coverage (`AssetPackLifecycleCoverage.AccountingAutomationStatus`), readiness projectors and
the typed blockers (`NegativeInterestUnsupported`, declared-exercise outcomes).

**Proposal.** Show a per-security card in the instrument passport (browser Data → Security Master,
`data-screen.security-master.ts`; WPF `SecurityMasterViewModel`). It is a row of capabilities:
- Terms
- Cash flows
- Accrual posting
- Valuation
- Corporate actions
- Redemption
- Reporting group

Each capability shows `Ready`, `Review` or `Blocked`, with the reason, the owner and the next action.
The next action is a deep link: "declare rate unit" opens idea #4's queue, and "swap legs not
supported" names the roadmap row.

Add a portfolio-level rollup in Portfolio: "37 holdings cannot accrue — 31 undeclared unit,
6 class unsupported".

**Why high value.** It turns silent non-coverage into an explicit, fail-closed statement. This
matches the truth discipline and answers a PM's or fund accountant's first question about a new
instrument.

**Implementation.** Add one shared read model (`SecurityCapabilityProfileDto`) in `Meridian.Contracts`,
composed from the pack descriptor plus per-record blockers, and served through the workbench query
service so both UI lanes read the same thing.

**Tradeoffs.** It must be derived, never hand-declared, or it will drift. Pin it with a test that
compares it to the actual gate outcomes.

## 3. Declarative accrual eligibility

**Today.** Recurring accrual admits only Bond. StructuredCredit can be calculated on the newer path
but isn't admitted. DirectLoan, CDs, commercial paper, repo and deposits post no recurring accrual.

**Proposal.** Replace class gating with a declared rule. A class is eligible when:
- its `CashFlowTermRoles` cover maturity, principal, coupon (or pays-none) and day count, **and**
- its lot unit (`SecurityLotModel`) is FaceValue, Notional or Commitment, **and**
- its pack names an approved journal template.

A record is admitted when its own blockers are clear. Roll classes on per pack through
`AssetPackAdmissionPolicy`, so turning DirectLoan on is a reviewed descriptor change plus a golden
accrual fixture, not new code.

**Operator moment.** In Accounting → recurring accrual drafts, loans and money-market holdings
appear with the same receipt and approval flow bonds use. Each draft shows the term-role
evidence it used.

**Tradeoffs.** Some classes have different accrual economics:
- DirectLoan: PIK and commitment fees
- Repo: start/end dates
- Discount instruments: no coupon

Admit the simple fixed-rate subset first and keep typed refusals for the rest.

## 4. Rate-unit repair queue

**Today.** Undeclared legacy rate units block projection with no bulk path. Repair happens one
record at a time through the `rateUnit` governed edit.

**Proposal.** Add a queue in Data → Security Master that groups blocked records by pattern: same
vendor, same class, same value magnitude band and same issuer. The operator selects a group, picks
a unit, attaches evidence (prospectus, vendor field definition) and submits **one batch of governed
drafts**. A different approver approves the batch, item by item or as a whole.

AI may *suggest* a unit from document extraction, labelled as a suggestion. Per D10, magnitude
alone never decides the unit.

**Value.** This unblocks cash flows and accrual for the legacy book in hours instead of weeks.
Each record keeps its own revision lineage.

**Tradeoffs.** Batch approval must not weaken separation of duties. Record per-item approval
receipts even when the batch is approved in one action.

## 5. Edit impact preview before approval

**Today.** `IAffectedLedgerBookResolver` and `IPeriodAwareRestatementResolver` already work out
which books and periods a change touches, but only *after* publication, for restatement.

**Proposal.** Call both resolvers at review time and show the approver a summary before approving,
for example: "Changing coupon 5.25 → 5.125 re-projects 14 open lots in 3 books; 2 accrual drafts
will be superseded; period 2026-10 is locked → restatement candidate required; 1 released report
references this security."

Add a dry-run cash-flow diff: old schedule vs new, plus the delta in accrued interest.

**Value.** The approver can see what approving will change. This is strong audit evidence: the
preview is retained with the approval.

**Tradeoffs.** Previews can go stale before publish. Bind the preview hash to the draft's base
version; the stale-proposal check from slice 2 already detects drift.

## 6. Reusable leg modules

**Today.** Swap legs are special-cased. Per-leg declared conventions and rate units are refused
(`ScheduleTermsWriteRules.cs:90-97`, `ScheduleSpecFactory.cs:136`).

**Proposal.** Model a `LegModule` (fixed, floating, inflation, premium, protection, funding) with
its own schedule conventions, rate unit and term roles. Compose instruments from legs:
- Swap = 2 legs
- CDS = premium + protection
- CapFloor = an option strip on a floating leg
- Swaption = an option plus an underlying swap of legs
- StructuredNote = a funding leg plus a payoff

The leg becomes the shared calculation unit for cash flows and accrual.

**Value.** One build unblocks Swap, Swaption and CapFloor cash flows, and gives CDS and notes a path.

**Tradeoffs.** This is the largest item. It needs a new write contract version under ADR-024's
readable-before-writable rollout and replay proofs for every existing swap.

## 7. CustomAsset → candidate pack promotion

**Today.** CustomAsset is the escape hatch: governed but opaque, so it gets no cash flows and no
accrual. `SecurityAssetPackRegistry.CreateCandidateDescriptor` and `ValidateCandidateSet` exist.

**Proposal.**
- Track how many CustomAsset records share a profile.
- When a profile crosses a threshold, offer "promote to candidate pack". This generates a candidate
  descriptor, a draft schema from the profile's approved fields, and a term-role mapping the operator
  fills in.
- The output is a reviewed pull request or design input, not a runtime class.

**Value.** User demand drives which asset class gets built next, and part of the work is drafted
automatically.

**Tradeoffs.** Code generation must stay a draft for developer review. Runtime-defined classes would
bypass the calculation kernels' type safety.

## 8. "What we knew when" compare

**Today.** Bitemporal reads exist (`GetByIdAsOfAsync`, `GetRecordedByIdAsOfAsync` in
`ISecurityMasterQueryService`). The 2026-07 brainstorm proposed the reads, and they are now built.

**Proposal.** Add a side-by-side compare in the passport: effective-as-of vs recorded-as-of, field
diff with the revision, maker, checker and source for each change. Link it from any report line's
proof drawer.

**Value.** An auditor can answer "why did the October report use 5.25%?" in one view.

**Tradeoffs.** It is mostly UI. Keep both lanes on one diff DTO.

## Synthesis

**Highest leverage:** #2, the capability card. It is medium effort, requires no new domain model,
and makes every gap explicit and actionable. It also creates the instrumentation that tells you
which of #3, #4 and #6 to fund first.

**Platform bets:** #1 (pack modules) makes #3, #6 and #7 cheap. #6 (leg modules) is the
cross-asset calculation unit for all derivatives.

**Cross-cutting:** #2, #3 and #5 all read pack descriptors together with per-record blockers. Build
one derived "security capability" read model and reuse it.

**Sequencing:** #4 and #2 first, together: quick operator value that unblocks the legacy book.
Next #5, because it strengthens the approval evidence that already exists. Then start #1 one
registry at a time. Then do #3 as the first consumer of pack-declared admission, and #6. #7 and #8
are opportunistic.

**Roadmap fit:**
- #2, #4, #5 and #8 fit the Portfolio / multi-asset coverage and evidence lanes
  (`W5X-EVIDENCE-001` for #5 and #8).
- #3 relates to `W9-NAV-006`.
- #1 and #6 need a new engineering row: a Security Master modularity follow-on to ADR-027.

**Competitive signals:**
- Institutional security masters (GoldenSource, Bloomberg PolarLake-style EDM) win by explaining
  coverage and data lineage, not by having more fields.
- Asset-servicing platforms expose instrument "servicing capability" per holding.
- Meridian's advantage is that its gaps are already typed and fail closed. Showing them (#2) and
  tying edits to their ledger impact (#5) is the most adaptable pattern and something few
  competitors can prove end to end.
