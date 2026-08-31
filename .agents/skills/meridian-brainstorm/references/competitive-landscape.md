# Competitive Landscape — Meridian Brainstorm Reference

Understanding the market helps identify where Meridian can differentiate vs. where it needs parity. The category set below follows the differentiation analysis in `docs/product/meridian-design-document.md` §1.4 and the market-entry wedge in §22.

**The gap Meridian sells against:** the market has tools for every fragment and no product for the whole proof. Portfolio systems *show* numbers but cannot prove them. Accounting systems *record* numbers but do not carry the operational evidence behind them. Close-management tools *track tasks* around numbers but own neither the data nor the ledger. Fund administrators *certify* numbers as a service, at service-provider speed and cost.

---

## Close and Controls Managers

**BlackLine, Trintech**

- Own the close checklist, task assignment, sign-off, and account-reconciliation certification workflow
- Sit *beside* the ERP/GL: they track and certify work about numbers they do not own
- Strong at: reviewer workflow, control evidence attachment, close status reporting, SOX-style certification
- Meridian's angle: it owns the data, the reconciliation, and the ledger, so the close checklist is derived from real record state rather than manually asserted. A task marked complete in Meridian is backed by a reconciled, approved record — not a checkbox.
- Worth borrowing: certification rigor, reviewer/preparer separation UX, close calendar ergonomics, materiality-driven task scoping

---

## Fund Administration and Private-Capital Suites

**Carta, FundStudio, Investran-class platforms**

- Capital accounts, commitments, closings, allocations, waterfalls, carry, fees, investor statements, notices
- Typically strong on fund economics, weaker on operational evidence chains and general-purpose accounting
- Meridian's angle: the same capital-account and waterfall math (`src/Meridian.Ledger/`) sits on a customer-neutral core — organization, entity, portfolio, account, book, period — so fund workflows are a specialization rather than the root model, and non-fund customers use the same spine
- Worth borrowing: LP statement quality, capital-call and distribution notice workflows, equalization handling, investor-facing clarity
- Not worth borrowing: fund-shaped root data models that make non-fund customers second-class

---

## Asset-Servicing and Portfolio Accounting Platforms

**SS&C Advent Geneva, eFront, Addepar-class platforms**

- Multi-book portfolio accounting, valuation, performance, and consolidated reporting at institutional scale
- Expensive, implementation-heavy, often service-wrapped; customization typically flows through the vendor
- Meridian's angle: self-hosted, contract-pack extensible, and evidence-first — the proof chain is a product object, not an export
- Worth borrowing: multi-book rigor, valuation methodology discipline, consolidation and elimination modeling, report library depth
- Not worth borrowing: implementation models that require vendor services for routine configuration

---

## Ledger and Payment APIs

**Modern Treasury and similar ledger/payment infrastructure**

- Developer-first double-entry ledger APIs, bank connectivity, and payment orchestration
- Excellent primitives, but the customer builds the operator product on top
- Meridian's angle: Meridian ships the operator product — queues, close cockpit, approvals, evidence, reporting — with the ledger as internal truth rather than an API surface to be assembled
- Worth borrowing: ledger API ergonomics, idempotency discipline, bank-format handling (BAI2/CAMT.053 — already in source at `src/Meridian.FinancialOperations/Reconciliation/Connectors/`)
- Boundary reminder: live payment execution remains a deferred lane (`docs/product/deferred-expansion-boundaries.md`); brainstorm toward the acceptance boundary, not past it

---

## Market Data and Trading Infrastructure

Relevant to the Trading, Strategy, and Data lanes, which remain part of the same governed spine.

- **Bloomberg Terminal / B-PIPE** — institutional gold standard, $20K+/user/year; strong lineage and monitoring expectations worth borrowing
- **Databento** — developer-first market data, cloud-only, strong data-product packaging and client ergonomics
- **Polygon.io** — simple REST/WebSocket access with a free tier; no local storage or workflow story
- **QuantConnect LEAN / Backtrader / Zipline** — backtesting ecosystems with their own ingestion; none carry an accounting or evidence lane
- Meridian's angle: decision-to-delivery continuity — research, backtest, paper validation, promotion governance, execution records, reconciliation, ledger, close, and delivery on one evidence model. Close tools have no trading lane; trading platforms have no close.

---

## Differentiation Matrix

Columns are category representatives, not exhaustive vendor claims. "Meridian now" reflects accepted roadmap evidence; "potential" reflects charter direction, not shipped capability.

| Capability | Close managers | Fund admin suites | Asset servicing | Ledger APIs | Meridian now | Meridian potential |
|---|---|---|---|---|---|---|
| Self-hosted | No | No | No | No | Yes | Yes |
| Owns the data *and* the ledger | No | Partial | Yes | Partial | Yes | Yes |
| Evidence chain as a product object | Partial | No | Partial | No | Yes (packets, manifests) | Number Passport on every figure |
| Reconciliation engine | Certification only | Partial | Yes | No | Yes | Sided matching on the live path |
| Close cockpit | Yes | Partial | Partial | No | Yes | Readiness scoring with named blockers |
| Governed report packs with lineage | No | Partial | Yes | No | Yes | Client-grade rendering |
| Fund economics (NAV, waterfall, carry) | No | Yes | Yes | No | Supported foundation | Wired economics path |
| Trading / research lane | No | No | Partial | No | Yes | Backtesting Studio evidence loop |
| Fail-closed truth discipline | No | No | No | No | Yes | Datum-level labeling |
| Contract-pack extensibility | No | No | Vendor-led | Partial | Partial | Certified asset packs |
| AI/MCP tooling surface | No | No | No | No | Yes | Governed-autonomy assistants |
| Cheap stakeholder verification seats | No | Partial | No | No | Partial | Free/low-cost reviewer roles |

---

## Meridian's Defensible Moats

1. **Proof as the product.** Provenance, evidence retention, reconciliation state, approvals, and report-line lineage are first-class, user-visible objects — not exports or logs.
2. **Truthful by construction.** Loud labeling of simulated data, fail-closed persistence, and `review-required` / `blocked` states instead of plausible-looking numbers. Honesty is expensive for competitors to retrofit.
3. **Decision-to-delivery continuity.** One governed evidence model spanning research through delivery; the codebase already carries both ends.
4. **Whole-balance-sheet modeling.** Assets, liabilities, commitments, guarantees, collateral, and contingent exposures as equal citizens via contract packs.
5. **Lower-risk adoption.** Shadow-mode onboarding proves value before a customer migrates official books.
6. **Trust as distribution.** Every governed delivery is a verification event; cheap reviewer seats spread the proof network.
7. **Self-hosted and hackable.** No per-query cloud fees, extensible in C# and F#, with an MCP surface for AI-native tooling.

---

## Good Borrowing Targets

- Certification and reviewer-workflow rigor from close managers
- LP statement and capital-activity notice quality from fund administration suites
- Multi-book and consolidation discipline from asset-servicing platforms
- Idempotency and bank-format handling from ledger APIs
- Data lineage and anomaly surfacing from institutional market-data vendors
- Client ergonomics and packaging polish from developer-first data products

## Bad Borrowing Targets

- Cloud-only assumptions that break self-hosted operation
- Vendor-service-dependent configuration models
- Fund-shaped root data models that demote non-fund customers
- Checklist-only close tracking that asserts completion without record backing
- Any surface that presents unwired capability as finished
- Features that ignore Meridian's seven-root navigation, evidence discipline, or the co-equal browser and desktop lanes
