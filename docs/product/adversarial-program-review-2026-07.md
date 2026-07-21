# Adversarial Program Review — Meridian (2026-07)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-07-21
**Scope:** whole-program review of Meridian's high-level functionality, focused on end-user value
**Method:** six parallel adversarial investigations over the *wired* code paths (truth/persistence,
execution & risk, ingestion & reconciliation, reporting & fund economics, browser workstation UX,
governance & security), plus a program-level structural pass. The two most severe claims were
independently spot-verified. Every finding is anchored to `file:line` so it is directly actionable.

> This document is deliberately critical. It gives fair credit to what is genuinely strong (see the
> final section), but its job is to surface where the running product falls short of the "prove the
> number" promise and what would most raise end-user value. It corroborates and sharpens the team's
> own W9 slate (`product-roadmap-priorities-2026-07.md`) and P0 tracker
> (`implementation-todo-list.md`) rather than contradicting them: no P0 row is production-certified,
> and this review confirms why with fresh evidence.

## Headline

Meridian's brand promise is **"does not just show the number — proves the number."** After tracing
the actually-wired code paths, the sharpest finding is:

> **The codebase is dramatically more capable than the running product.** Meridian has been built
> *breadth-first*: several of its most valuable capabilities exist as well-written, well-tested
> libraries that are **never wired into the live path**. The product a user actually runs is thinner,
> emptier, and less trustworthy than the repository makes it look — and on a default launch it
> silently substitutes fabricated / in-memory data for real evidence, which is the one thing a
> "prove the number" product cannot do.

This is not the usual "it's a stub" critique. The hard parts are frequently *done* — they're just
disconnected. That is both the bad news (apparent capability is partly illusory) and the good news
(the highest-value fixes are wiring and truth-telling, not green-field building).

## The pattern that explains most of the value loss: "built but not wired"

Four independent investigations converged on the same shape:

| Capability that exists and is largely correct | Where it lives | What actually runs instead | Evidence |
| --- | --- | --- | --- |
| **Sided reconciliation matcher** (staged exact → tolerance → candidate; variance-based confidence) | `StatementMatchingEngine.cs` | A per-row **self-check** that never consults the internal book | `StatementRunWorkflowService.cs:22` instantiates `StatementMatchingService`; the sided engine is only reachable from a non-live service |
| **Client-grade PDF/XLSX renderer** (QuestPDF + ClosedXML, typed cells, multi-sheet) | `Meridian.Documents/FinancialReportDocumentRenderer.cs` | A hand-built **plain-text** PDF and an **all-text-cell** XLSX | `Meridian.Documents.csproj` referenced only by test projects; prod producer is `ReportingCertifiedArtifactProducer.cs:410` |
| **Unitized NAV, European waterfall w/ GP catch-up, preferred return, capital calls, clawback** | `Meridian.Ledger/*Calculator.cs`, `*Projector.cs` | The only *wired* fee calc is un-prorated, hurdle-less, with a 4×/12× over-charge trap | Advanced calcs have no non-test callers; wired one at `AutomatedJournalEventProducers.cs:386` |
| **"Explore with sample data" onboarding pack** | `sample-pack.json` (written by `FirstRunExperienceService.cs`) | Nothing reads it back; every screen renders empty on a fresh install | No code path reads `sample-pack.json`; read models resolve empty live services |

The uncomfortable implication: a demo or evaluation of the *running* product shows far less than the
repo can do — and much of what it does show on a default launch is not real.

## Findings by end-user value area

### A. Truth & data integrity (brand-critical)

- **Default launch is silently synthetic + in-memory.** The fail-closed guard against in-memory/fake
  stores only engages when a production posture is explicitly declared
  (`ProductionServiceRegistrationPolicy.cs:160` — `ASPNETCORE_ENVIRONMENT=Production`,
  `MERIDIAN_MODE=Live`, etc.). A plain `dotnet run` resolves to non-production, so
  `InMemoryBankingService`, `InMemoryFundStructureService`, and peers are active; ledger/banking/NAV
  state **evaporates on restart** while the UI looks fully functional. ~48 non-test source files use
  in-memory stores.
- **Provenance is page-level, not per-datum.** A good red "SIMULATED / PERSISTENCE: NONE" banner
  exists, but individual numbers (backfilled synthetic OHLC, fixture metrics, demo prices) carry no
  flag that reconciliation, NAV, or report-pack certification honor. Backfilled synthetic bars read
  back as genuine history.
- **A fabrication route sits in shared endpoint code.** `POST /api/dev/seed/bank-transactions`
  (`BankingEndpoints.cs:254`) resolves whatever `IBankingService` is registered —
  `PostgresBankingService` in a real deployment — and writes `Random(42)` money-movement rows into
  the authoritative bank table, guarded only by a code comment. *Verified nuance:*
  `MapBankingEndpoints` is currently never called, so it is not live today; it is an unguarded
  fabrication path one wiring change away from production.
- **Improvement:** make non-production persistence an explicit opt-in-or-throw for every money-path
  domain (the governance profile already does this — extend the pattern); propagate `IsSimulated`
  provenance into stored records so certification can refuse/watermark it; delete or hard-gate the
  seed endpoint. **Value: very high. Effort: M.**

### B. Reconciliation — "theater on the shipping path"

The most damaging finding for a product whose wedge is a *reconciliation control tower*.

- The matcher that ships compares each statement row **to itself**, not to the internal book.
  Positions with any symbol and quantity > 0.0001 are declared "matched" at a hardcoded **0.95**
  confidence; cash lines only "match" when the amount is ~zero, so every real deposit/trade becomes a
  break; `ToleranceBreached` is hardcoded `true` (verified in `BrokerStatementInfrastructure.cs:411`,
  the "matched reference" is literally the row's own source number).
- **No FX** on any live reconciliation path (`IFxRateProvider` has no implementation) — cross-currency
  custody cannot reconcile.
- **No institutional formats:** only CSV/OFX/IB-Flex/Alpaca ingest; camt.053, BAI2, MT940, SWIFT
  appear only in docs. Most bank cash statements cannot be ingested without hand-conversion.
- **Improvement:** wire the existing `StatementMatchingEngine` into the live workflow, feed it real
  ledger/portfolio populations, extend it to cash/transaction rows, add a real FX provider, and add
  camt.053/BAI2 connectors (the `IStatementConnector` seam is clean). **Value: very high — this IS the
  wedge. Effort: L.**
- **Fair credit:** the *ingestion* layer is genuinely strong — tolerant OFX (1.x SGML + 2.x XML),
  XXE-safe Flex parsing, Levenshtein column-confidence scoring, format-drift SHA-256 detection, and
  real provider data-quality validation on the market-data path.

### C. Reporting & fund economics

- Production report pack = a monospaced **text-dump PDF** (hard-fails on any non-ASCII fund/investor
  name — accents, £/€) and an XLSX where **every cell is text**, so Excel will not SUM/pivot without
  retyping. This is literally the roadmap's "re-type every deliverable into Excel."
- The correct math (NAV, waterfall, preferred return, capital calls, clawback, direct-lending
  servicing) is implemented and largely correct — but test-only. The one production-wired fee accrual
  applies an **annual rate to sub-annual periods** (4×/12× over-charge) with no hurdle.
- The partners'-capital statement is real but **per equity-account, not per-LP**, and relies on
  fragile string heuristics (e.g. `Name.StartsWith("Cash")`, account name equals `"Retained
  Earnings"`).
- **Improvement:** DI-register `FinancialReportDocumentRenderer` as the report binary renderer; wire
  the NAV/waterfall projectors into the accrual/report pipeline; add day-count proration + hurdle +
  per-unit high-water to fee accrual; build a true per-LP capital statement. **Value: high.
  Effort: M–L.**

### D. Execution, paper trading & safety

- **Alpaca live-fill loop is broken:** the gateway writes execution reports only synchronously at
  submit/cancel (never `Filled`); there is no `trade_updates` WebSocket or status poll, so
  `ProcessFillReportAsync` never runs — live orders stick "Accepted" forever and the ledger never
  records the trade (`AlpacaBrokerageGateway.cs`).
- **Paper fills are cost-free and instantly matched** (a limit-buy at $1 fills at $1; commission 0),
  and the promotion gate reads Sharpe/return computed from those fills — so overfit strategies pass
  paper and lose money live. (Backtest fill models, by contrast, are realistic — the ladder loses
  realism exactly where it should add it.)
- **WPF shell "Flatten" / "Cancel All" (Danger-toned) are review-only no-ops** — they float a pane and
  do nothing (`TradingWorkspaceShellPresentationService.cs:166`). A safety button that silently does
  nothing is worse than none. (The browser cancel-all and the WPF blotter flatten *are* wired — so
  demote or wire the shell ones.)
- **No fat-finger / max-notional / price-collar controls** — the only size gate is share quantity, so
  a mis-scaled or fat-fingered notional order sails through.
- **Improvement:** add an Alpaca fill consumer; give paper gateways the backtest fill model (cost,
  slippage, price-touch matching) and reject cost-free runs for promotion; wire or demote the shell
  safety buttons; add notional/collar risk rules. **Value: high (correctness + trust). Effort: M.**

### E. Governance & security — the gap under the brand

- **Multi-tenancy is fail-open by design:** a tenantless caller gets **no filter → every tenant's
  rows** (`TenantReadPredicate.cs:32`); the write gate is off-by-default and only logs; the guard
  fails open on registry error; and the default admin is created tenantless. That is cross-tenant
  financial disclosure in any shared deployment.
- **Audit is tamper-evident, not tamper-proof:** unkeyed SHA-256 chains with no HMAC/signature/
  anchoring — a DBA can edit a row and recompute the chain undetected. The **authoritative journal
  ledger has no hash chain at all**.
- **The segregation-of-duties / dual-approval / MFA engine is dead code on money paths** — its only
  caller is an advisory endpoint whose approver identity is *client-supplied* and whose MFA check can
  never be true. Direct-lending payment release and break closure are single-permission, no four-eyes.
  (Reporting and manual-journal approvals *are* properly server-enforced — the pattern exists; it is
  just not applied to the money paths.)
- **Non-uniform route authorization:** roughly 106 of ~1,139 routes carry permission gates; whole
  groups (archive maintenance, data-quality, packaging/export, storage, failover) have none, so any
  authenticated read-only user can trigger destructive/exfil operations. The "coverage test" the code
  comments claim exists only checks name uniqueness.
- **Secret leaks past the (otherwise sound) vault:** OAuth tokens, Robinhood tokens (in
  `HKCU\Environment`), and Alpaca keys (in `appsettings.json`) are stored in plaintext; the Linux
  vault key sits next to the ciphertext.
- **Improvement (in order):** flip tenancy fail-closed + backfill tenant stamps → route real sensitive
  mutations through the SoD engine with server-derived identity + real MFA → hash-chain the ledger
  with a keyed/anchored scheme → blanket RBAC + a real coverage test → route stray secrets through the
  vault. **Value: high (blocks any real multi-tenant/enterprise deployment). Effort: L.**

### F. First-run / onboarding

- The "Explore with sample data" path (the *recommended* option) writes a file nothing reads, and the
  rich dev fixtures are compiled out of production builds — so a new operator lands on **seven screens
  of zeros with no in-context next action**, after a wizard that promised data. The guided tour then
  walks them through the empty screens.
- **Improvement:** seed durable sample data into the read models (shared by dev and prod), and add
  first-class per-workspace empty states with a primary CTA (import / connect / load sample). The
  `EmptyState` component already supports actions — they are just not used. **Value: high (this is the
  entire first impression). Effort: M.**
- **Fair credit:** the UI itself is genuinely mature — ~195K lines, all seven nav areas deep (not
  skeletons), strong accessibility (jest-axe, ~1,157 aria-labels), sophisticated loading/error/
  degraded handling, and real mutations wired throughout. The problem is data-starvation, not dead UI.

## Program-level concerns (beyond any single subsystem)

1. **Scope vastly outruns delivery capacity.** 52 projects, ~750K lines of C#, a 129K-line second UI
   (WPF) admittedly incomplete on parity, plus market data + trading + backtesting + fund accounting +
   direct lending + reconciliation + reporting + MCP tooling — against incumbents like Carta,
   FundStudio, and Modern Treasury. The design document has expanded across nine versions, each adding
   scope. Breadth is the enemy here: it is *why* so much is built-but-not-wired. **The single most
   valuable strategic move is to pick one thin end-to-end slice (import → sided reconcile → governed
   client-grade report, on durable storage) and make it real and trustworthy before widening.**
2. **Incompleteness is hidden, not labeled.** Only ~5 TODO/FIXME markers in 750K lines, despite ~40
   self-acknowledged blocking gaps. Unwired capability and stubs look identical to finished work
   in-editor — which is how a team convinces itself it is further along than it is.
3. **The second UI (WPF) is a large, ongoing tax.** 129K lines chasing browser parity that is
   explicitly unfinished. For end-user *value*, doubling every surface across two clients is hard to
   justify until one client is trustworthy end-to-end.
4. **Maintainability hazards:** several 4,000–7,400-line files (`settings-screen.tsx` ~7,428;
   `AccountingConfigureViewModel.cs` ~5,556; `WorkstationEndpoints.cs` ~4,630) concentrate risk and
   slow every change.
5. **Governance/docs ceremony can outweigh product depth.** ~86K lines of docs, a YAML roadmap
   registry with schemas/decision-logs/stage-gates, and an MDIF framework represent real rigor — but
   it is process value, not end-user value, and the elaborate certification language can create a
   *sensation* of production-readiness the shipping code does not yet earn.

## Prioritized improvement list (by end-user value uplift)

| # | Improvement | Why it is high-value to the end user | Effort |
| --- | --- | --- | --- |
| 1 | **Wire the real sided reconciliation matcher + FX + ledger populations into the live path** | Makes the core "control tower" promise actually true; today reconciliation is theatrical | L |
| 2 | **Seed durable sample data into read models + actionable empty states** | Fixes the entire first hour; converts "impressive but empty" into "impressive" | M |
| 3 | **Make the default run tell the truth** (opt-in-or-throw persistence, per-datum provenance, remove the seed route) | Restores the one non-negotiable: no fake-looking-real numbers | M |
| 4 | **Connect the client-grade PDF/XLSX renderer + wire NAV/waterfall math; fix fee proration** | Ops teams stop re-typing deliverables into Excel; the "hard math" finally reaches a report | M–L |
| 5 | **Fail-closed tenancy + hash-chain the ledger + enforce SoD/MFA on money paths + blanket RBAC** | Unblocks any real multi-tenant/enterprise deployment; makes governance real, not branded | L |
| 6 | **Fix Alpaca fill loop; realistic paper fills; wire/demote safety buttons; add notional controls** | Trades actually book; the promotion gate stops laundering overfit strategies; safety buttons stop lying | M |
| 7 | **Strategically: pick one end-to-end slice, defer the rest (incl. WPF parity); split the giant files** | Concentrates finite capacity on making *something* trustworthy end-to-end | ongoing |

Items 1–4 are almost entirely **connect-and-truth work on things already built** — the highest ROI
available.

## What is genuinely strong (so fixes do not regress it)

The mature browser UI and accessibility; realistic backtest fill models; robust ingestion/parsing
(OFX, Flex, column-confidence, drift detection); real provider data-quality/freshness/gap validation;
correct fund-accounting math (NAV, waterfall, preferred return, clawback, direct-lending servicing);
sound authentication (PBKDF2 @ 210k, hashed sessions, CSRF, HTTPS guard); server-enforced
reporting/manual-journal four-eyes approvals; substantial test coverage (~393K test LOC); and a
candid internal readiness tracker that already names most of these gaps.

## Relationship to existing planning

Every finding here maps onto an existing W9 slate row or P0 tracker item; this review does not open a
new planning lane. Its contribution is (a) independent confirmation with fresh `file:line` evidence,
(b) the cross-cutting "built but not wired" framing that reprioritizes the slate toward
connect-and-truth work, and (c) the program-level observation that focus — not more surface — is the
highest-leverage move.
