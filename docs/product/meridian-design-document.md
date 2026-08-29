# Meridian Design Document — Version 1.0

**Status:** canonical
**Owner:** core-team
**Reviewed:** 2026-08-03
**Supersedes:** Version 0.25 (full text preserved at
[`archive/docs/design/meridian-design-document-v0.25.md`](../../archive/docs/design/meridian-design-document-v0.25.md))
**Source:** Ground-up rewrite of the 0.15–0.25 charter lineage. Grounded in the roadmap registry
(`docs/roadmap/data/*.yml`, snapshot 2026-08-03), the program state and P0 readiness tracker, the
source-module registry, repository measurements taken 2026-07-28, the 2026-07 adversarial program
review (`docs/product/adversarial-program-review-2026-07.md`), and the accepted W9 priority slate
(`docs/product/product-roadmap-priorities-2026-07.md`, decision `DEC-PRIORITY-SLATE-001`). The
version-by-version history of the superseded lineage is summarized in Section 25.

---

## 1. Value Proposition

### 1.1 The Problem Meridian Sells Against

Investment organizations run on numbers nobody can cheaply verify. Positions live with custodians,
cash lives with banks, terms live in documents, economics live in spreadsheets, entries live in a
general ledger, and deliverables are re-typed into Excel and PDF at the end of every period. Trust
is assembled by hand: operators reconcile after the fact, controllers sign what they cannot fully
trace, and auditors reconstruct lineage months later at billable rates.

The market has tools for every fragment and no product for the whole proof:

* Portfolio systems **show** numbers but cannot prove them.
* Accounting systems **record** numbers but do not carry the operational evidence behind them.
* Close-management tools **track tasks** around numbers but do not own the data or the ledger.
* Fund administrators **certify** numbers as a service, at service-provider speed and cost.

The unserved product is the number itself, delivered with its proof attached.

### 1.2 The Product Promise

> **Meridian sells proven numbers.** Every figure Meridian publishes carries a reconstructable
> chain — source evidence, normalization, validation, reconciliation, ledger impact, approval,
> report usage, delivery — so finance teams close faster, operate leaner, and hand every
> stakeholder an answer they can check.
>
> Ask any question. Verify every answer.

A number is **proven** when its evidence chain is current, reconciled, approved, and
reconstructable on demand. That definition is the product's unit of value, its quality bar, and its
primary metric (Verified Coverage, Section 1.5). Meridian is not a dashboard with an audit trail
bolted on; it is the governed financial system underneath the dashboard.

The customer buys three compounding outcomes:

1. **Audit-ready truth on demand.** The proof chain is produced by the workflow itself, not
   assembled after it. Close binders, NAV support, and statement packages fall out of normal
   operation.
2. **Operating leverage under governance.** Routine ingestion, matching, drafting, and packaging
   are automated inside deterministic controls, so one operator governs more entities, books, and
   accounts without weakening approval, evidence, or period-lock discipline.
3. **A close that compounds.** Every resolved exception, approved mapping, and certified dataset
   makes the next period faster. Proof is an asset that accrues, not a cost that recurs.

The operating question every surface must answer remains:

> Can Meridian prove, book, reconcile, approve, and report this number?

And the design law for every operator action remains:

1. What changed?
2. Who approved it?
3. Why did it remain blocked?

### 1.3 What Meridian Is — and Is Not

Meridian is a modular, configurable, self-hosted financial operations platform for fund
administrators, private fund managers and fund CFOs, registered investment advisors, family
offices, and hybrid institutional teams. Fund management is a first-class **specialization**, not
the platform root model: core language, contracts, and new architecture use customer-neutral
concepts — organization, entity, portfolio, account, book, period, transaction, operational event,
obligation, evidence, approval, journal, report, audit trail — and reserve fund, investor, capital
account, and fund event for workflows that are explicitly fund/private-capital specific.

For ledger records, **Meridian is the source of all ledger truth**. External accounting systems
contribute read-only evidence and reconciliation signals unless an approved publishing workflow
explicitly exports Meridian-owned entries.

Meridian is **not**:

* a trading OMS/EMS, CRM, or cap-table platform,
* a payment processor or live bill-pay operation,
* a broad self-service investor portal,
* a mobile-first product (there is no mobile lane; see Section 5.4),
* a generic no-code workflow builder,
* an enterprise risk or forecasting suite detached from operational records,
* an outsourced services operation,
* an autonomous accounting agent. AI may extract, match, summarize, detect discrepancies, and
  draft — it must never bypass operator approval, retained evidence, ledger controls, period
  locks, segregation of duties, report release checks, or payment controls (Section 21).

### 1.4 Differentiation

Seven themes separate Meridian from adjacent products (close managers such as BlackLine and
Trintech, fund-administration suites such as Carta and FundStudio, asset-servicing platforms such
as eFront and SS&C Advent Geneva, and ledger APIs such as Modern Treasury):

1. **Verifiable financial data.** Provenance, evidence retention, reconciliation state, approvals,
   and report-line lineage are first-class, user-visible product objects — culminating in the
   Number Passport (Section 22).
2. **Truthful-by-construction operations.** The product never lies about its own state. Simulated,
   sample, or synthetic data is loudly labeled; unsupported persistence fails closed; missing or
   stale evidence renders as `review-required` or `blocked`, never as implicit success. Honesty is
   a feature competitors cannot retrofit cheaply (Section 2.3).
3. **Decision-to-delivery continuity on one spine.** The same governed evidence model spans
   strategy research, backtesting, paper validation, live-promotion governance, execution records,
   reconciliation, the ledger, the close, and stakeholder delivery. Close tools have no trading
   lane; trading platforms have no close. Meridian's codebase already carries both (Section 5).
4. **Whole-balance-sheet modeling.** Assets, liabilities, commitments, guarantees, collateral, tax
   obligations, intercompany balances, and contingent exposures are modeled as equal citizens
   through contract packs (Section 22).
5. **Lower-risk adoption.** Shadow-mode onboarding produces read-only parallel views,
   opening-balance reconciliations, evidence-backed consolidation reports, and close-readiness
   scores before a customer migrates official books.
6. **Contract-driven extensibility.** New asset and liability coverage arrives through governed
   packs — schemas, lifecycle events, valuation methods, accounting rules, validations, reporting
   taxonomy — without redesigning the core ledger (Sections 20, 22).
7. **Collaborative distribution.** Auditor, tax, attorney, investment-manager, valuation, banking,
   investor, family-member, and advisor roles are inexpensive or free to add, so evidence review,
   document requests, approvals, and report delivery expose Meridian to the customer's entire
   professional network (Section 2.4).

### 1.5 North-Star Metrics

Verified Coverage is the primary metric; the others make it actionable. All are product design
targets that must be implemented as honest, evidence-backed telemetry — none are shipped-metric
claims until the roadmap registry accepts them.

| Metric | Definition | What it drives |
| --- | --- | --- |
| **Verified Coverage** (primary) | Percentage of reported assets and liabilities that are current, reconciled, approved, and linked to supporting evidence. | The product promise itself; the sales demo; the renewal story. |
| Time to First Proof | Elapsed time from a fresh install to the first evidence-backed, reconciled, reportable number. | Onboarding quality; the seeded-demo and durable-storage work (`W9-DEMO-002`). |
| Proof Latency | Elapsed time from source arrival to verified (reconciled + approved) state for a record class. | Operational speed; close compression. |
| Governed Touchless Rate | Share of routine, policy-eligible items advanced without per-item operator touch, always inside approved policy, materiality caps, and retained evidence (Section 21). | Operating leverage without governance erosion. |
| Activation Ratio | Share of shipped capability reachable by an operator in the running product. | The activation-over-expansion strategy (Section 2.1); keeps built-but-unwired capability visible as debt, not as breadth. |

---

## 2. Value Delivery Strategy

This section is the program-level strategy for realizing Section 1's promise. It is deliberately
grounded in two evidence sources: the 2026-07 adversarial program review and the accepted W9
priority slate (`DEC-PRIORITY-SLATE-001`).

### 2.1 Activation Over Expansion

The adversarial review's headline finding: the codebase is dramatically more capable than the
running product. High-value capabilities exist as well-tested libraries that were never wired into
the live path. The highest-return engineering available is therefore **activation** — connecting,
labeling, and proving what is already built — not net-new surface. The prior charter lineage grew
scope across nine versions; this version grows none.

The activation register (status labels follow Section 24's claim rules; "supported foundation"
means the capability exists in source with tests but is not the wired operator path):

| Dormant capability (source evidence) | Status | Activation lane |
| --- | --- | --- |
| Sided statement-vs-ledger reconciliation matching (`StatementMatchingEngine`, `src/Meridian.FinancialOperations/`) | Supported foundation; live path still uses a weaker per-row check | `W9-INGEST-009` |
| Institutional bank formats (`Bai2StatementConnector`, `Camt053StatementConnector`, `src/Meridian.FinancialOperations/Reconciliation/Connectors/`) | Supported foundation in source; registry acceptance planned | `W9-INGEST-009` |
| Client-grade PDF/XLSX rendering (`ClientGradeReportRenderer`, `FinancialReportDocumentRenderer`, `src/Meridian.Documents/`) | Activated; deterministic PDF/XLSX is the certified reporting path, with the bespoke partners-capital layout delivered. Accepted 2026-08-29 (`DEC-W9-ACCEPTANCE-001`) | `W9-REPORT-005` |
| Unitized NAV, fee accruals with hurdles, European waterfall, preferred return, clawback, equalization (`NavPerUnitCalculator`, `EuropeanDistributionWaterfall`, `PreferredReturnCalculator`, `CarriedInterestClawbackCalculator`, `EqualizationCalculator`, `src/Meridian.Ledger/`) | Activated; ledger-backed economics with golden-file worked examples. Accepted 2026-08-29 (`DEC-W9-ACCEPTANCE-001`) | `W9-NAV-006` |
| Broker fill streaming into order and ledger state (`AlpacaBrokerageGateway`, `src/Meridian.Execution/`) | Implementation verified complete; held at the 2026-08-29 acceptance review over three recorded caveats on the fill-to-ledger path | `W9-ALPACA-004` |
| Realistic fill and cost models (`MarketImpactFillModel`, `OrderBookFillModel`, commission models, `src/Meridian.Backtesting/`) | Activated; both paper gateways match and cost through the shared documented policy. Accepted 2026-08-29 (`DEC-W9-ACCEPTANCE-001`) | `W9-PAPER-003` |
| Kill-switch, cancel-all, and pre-trade notional/collar controls | Partial foundation; WPF safety surfaces must be wired or visibly demoted | `W9-SAFETY-007` |
| Hash-chained audit for the accounting ledger; route-level authorization; fail-closed tenancy (`AuditChainService` exists for storage; the journal ledger chain and blanket route coverage do not) | Partial foundation | `W9-GOV-008` |
| Asset accounting event spine with atomic lot posting (`AssetAccountingEventSpineService`, `src/Meridian.FinancialOperations/Ledger/`) | Complete | `W9-ASSET-010` |
| Corporate action approval and posting lane (`CorporateActionOperationsService` in `src/Meridian.Application/SecurityMaster/CorporateActions/`, `CorporateActionAccountingProjectionService` in `src/Meridian.Instruments/AssetOperations/`, `PostgresCorporateActionOperationsStore` in `src/Meridian.Storage/SecurityMaster/`) | Ingest, case operations, and accounting projection are delivered and accepted; the approval lane is modelled in the contract but unreachable in the shipped implementation, so no case can reach `ReadyForApproval` or any later state | `W9-CORPACT-011` |
| Operational Evidence Graph as a shared product surface | Planned; explorer, proof-drawer, and manifest primitives exist | `W5X-OEG-001` |

Rules of the doctrine:

* New scope requires a roadmap registry row; activation of existing scope is the default lane.
* An activated capability must replace its weaker predecessor on the live path, not sit beside it.
* Capability that stays dormant must be visibly labeled as unwired in operator-facing coverage
  surfaces — unwired and finished must never look identical.

### 2.2 The Proven Slice Doctrine

The unit of product progress is a **proven slice**: one end-to-end path a real operator can run on
durable storage with truthful states and retained evidence. The foundational slice (Section 23) is
the template: controlled import → validation → reconciliation → review → approval → evidence →
reporting.

A slice is proven only when all of the following hold:

1. It runs end-to-end in the operator product — not only in tests or libraries.
2. It runs on durable storage; in-memory stand-ins fail closed in supported production profiles.
3. Every state it displays is truthful — simulated inputs are labeled, missing evidence blocks.
4. Its evidence is retained and reconstructable (Section 11).
5. Its acceptance is recorded in the roadmap registry with linked evidence.

Widening (new asset classes, new surfaces, new packages) is justified only from a proven slice
outward. The W9 ordering encodes this: truth before demonstration (ranks 1–2), honest gates before
more surface (3–4), the deliverable is the product (5–6), never overpromise safety or governance
(7–8), widen trusted intake last (9).

### 2.3 Truth Discipline

Truthful operation is a product feature with hard rules:

* Every simulated, sample, synthetic, or fixture-derived value visible to an operator carries a
  loud, unambiguous label, at the datum level where feasible — not only at page level.
* Unsupported persistence (in-memory stores, placeholder services) fails closed in supported
  production profiles rather than silently substituting for durable storage (`W9-TRUTH-001`).
* A fresh install reaches a truthful, populated, durable demonstration workspace through one
  documented command (`W9-DEMO-002`); empty screens offer a concrete next action instead of zeros.
* Development seeding surfaces are unreachable in production profiles by construction, not by
  comment.
* Missing, stale, or unsupported source evidence produces `review-required` or `blocked` states,
  never plausible-looking operational data.
* Gates must measure what they claim: paper-trading fills honor limit/stop semantics and trading
  costs before their statistics feed promotion decisions (`W9-PAPER-003`).

The registry's `RISK-SIM-REAL-001` (simulated data presenting as real) is the named existential
risk this discipline retires.

### 2.4 Trust as Distribution

Proof travels with the number. Every governed package Meridian delivers — investor statement, NAV
support, close binder, audit response — is a frozen snapshot whose lines can be traced to evidence
(Section 18). Recipients experience verification directly: an auditor who receives a Meridian
package can drill from any line to its support without emailing the operator.

That makes every delivery a distribution event. The design consequence: stakeholder-facing
verification views are governed, entitlement-scoped, read-only surfaces attached to delivered
packages — inexpensive to grant, cheap to operate. A broad self-service portal remains deferred
(Section 24); governed package delivery with verifiable drill-through is the wedge.

### 2.5 Packaging and Monetization Posture

Design-level posture, not committed pricing:

* **Enter with the Close, Data and Evidence Control Tower** (Section 22): sit above existing
  spreadsheets, GLs, portfolio systems, banks, custodians, and document stores in shadow mode;
  prove value through reconciliations, close readiness, and verified reporting before owning
  official books.
* **Price the governed estate, not the operator seat.** Value scales with entities, books,
  accounts, and certified outputs under governance — the measures Verified Coverage is computed
  over — not with the number of operators. Charging per seat would punish the operating leverage
  the product creates.
* **Make verification cheap to spread.** Stakeholder and reviewer access (auditor, LP, trustee,
  advisor) should be inexpensive or free; each grant extends the proof network and seeds the next
  customer.
* **Stage to native accounting.** Control tower first; native multi-book ledger and private-capital
  accounting second; ecosystem (connector SDK, certified packs, template marketplace) third
  (Section 22).

---

## 3. Market and Customers

### 3.1 Primary Customer Types and the Outcome Each Buys

| Customer type | Primary needs | Headline outcome purchased |
| --- | --- | --- |
| Fund Administrators | Reconciliation, NAV support, investor reporting, capital activity, audit evidence, workflow management | Administrator-grade control plane whose every deliverable is evidence-backed |
| Private Fund Managers / Fund CFOs | Fund operations, capital accounts, fund events, portfolio valuations, tax/audit support, LP reporting, data exports | Shadow books and close cockpit that prove the administrator's numbers — then become the books |
| Registered Investment Advisors | Portfolio operations, client reporting, data aggregation, performance review, advisor workflows, compliance support | Aggregated, reconciled client records with governed report delivery |
| Single Family Offices | Entity management, trust and beneficiary reporting, alternative assets, treasury, consolidated reporting | Whole-balance-sheet visibility across entities with verifiable consolidation |
| Hybrid / Institutional Users | Configurable workflows across operations, reporting, financing, planning, governance | One governed operational record across desks and functions |

### 3.2 Product Strategy

One platform with configurable tenant profiles — not separate applications per customer type.
Example profiles: Fund Administrator, RIA, Single Family Office, Private Credit / Alternative
Asset, Hybrid Institutional.

Authority is scoped inside profiles: production authorization must answer whether a user holds a
role or permission **for a specific tenant, organization, fund, portfolio, legal entity, account,
book, period, document, report package, delivery record, or amount limit** — not merely globally
(Section 14).

---

## 4. Users, Stakeholders, and Operating Journeys

### 4.1 Primary Operator

The **Financial Operations Professional**: responsible for ensuring financial data is accurate,
complete, reconciled, auditable, and available to support decisions. The persona includes fund
administrators, investment accountants, operations analysts, portfolio operations specialists,
treasury operations personnel, RIA operations staff, family office operations staff,
reconciliation specialists, and reporting analysts.

The primary operator workflow — and the platform's canonical spine:

```text
Import
→ Validate
→ Reconcile
→ Investigate
→ Approve
→ Report
```

### 4.2 Persona Matrix (condensed)

| Persona | Category | Core goal | Frequency |
| --- | --- | --- | --- |
| Financial Operations Professional | Primary Operator | Accurate, reconciled, auditable financial data | Daily |
| Investment Accountant | Primary Operator | Accurate accounting and reporting support | Daily |
| Reconciliation Analyst | Primary Operator | Resolve breaks quickly and clearly | Daily |
| Fund Accountant | Primary Operator | NAV support, fund reporting, investor activity | Daily / Monthly |
| Operations Manager | Primary Operator / Manager | Operational health and team workload | Daily |
| Data Operations Analyst | Primary Operator | Healthy pipelines and provider feeds | Daily |
| Treasury Operations Specialist | Primary Operator | Liquidity and cash movement control | Daily |
| Reporting Analyst | Primary Operator | Accurate reports and packages | Daily / Monthly |
| Portfolio Manager | Investment User | Monitor and manage portfolio outcomes | Daily / Weekly |
| Investment Analyst | Investment User | Research investments and opportunities | Daily |
| Quantitative Researcher | Investment User | Develop and validate strategies | Daily / Weekly |
| Trader | Investment User | Execute or monitor trading activity | Daily |
| Risk Manager | Governance / Investment User | Monitor investment and operational risk | Daily / Weekly |
| CFO | Executive | Financial accuracy and liquidity oversight | Weekly / Monthly |
| CIO | Executive | Portfolio strategy and risk oversight | Weekly / Monthly |
| Controller | Governance | Accounting governance and audit readiness | Weekly / Monthly |
| Compliance Officer | Governance | Policies and controls followed | Weekly / Monthly |
| Fund Investor / LP | Stakeholder | Performance and capital activity | Monthly / Quarterly |
| RIA Client | Stakeholder | Personal portfolio and advisor reports | Monthly / Quarterly |
| Family Beneficiary | Stakeholder | Family assets and distributions | Monthly / Quarterly |
| Trustee | Stakeholder | Fiduciary oversight | Monthly / Quarterly |
| Board / Investment Committee Member | Stakeholder | Governance materials and strategic reporting | Quarterly |
| Auditor | External / Governance | Verify accuracy and evidence | Quarterly / Annual |
| System / Security / Integration Administrator | Administration | Platform health, access, and connections | Daily / Weekly |

### 4.3 Operating Journeys

Each journey defines what Meridian must retain for each decision. Full field-level requirements
live with the owning workflows (Sections 6, 11, 18).

| Journey | Trigger | Primary surface | Output | Retained evidence |
| --- | --- | --- | --- | --- |
| Data Operations daily workflow | Provider/file arrival, provider-health or schema-drift alert, downstream blocker | `Data` import-run queue and provider validation packets | Certified import run, rejected run with repair instructions, replay request, or blocked downstream output | Import Run Evidence Contract |
| Fund Accountant monthly close | Period close opens, NAV support due, administrator package arrives, late activity | `Accounting` close cockpit, ledger explorer, capital account workbench | Closed period, NAV support, investor statements, or blocked-close report | NAV Readiness Packet |
| CFO control review | Weekly control meeting, close checkpoint, material exception, package deadline | Executive control brief across `Accounting`, `Reporting`, `Portfolio` | Approved actions, held packages, escalations, board-ready summary | Executive Financial Control Brief |
| Compliance review | Access certification, audit request, permission change, legal hold | `Settings` access review, audit timeline, evidence vault | Certified/revoked access, audit response, policy exception, legal-hold evidence set | Audit Event Catalog + Scoped Access Review Packet |
| Portfolio Manager daily review | Start of day, material change, breach, stale mark, valuation exception | `Portfolio` daily review with proof drill-down | Acknowledgements, escalations, valuation-review requests, held commentary | Portfolio Daily Review Packet |
| Stakeholder report package | Capital call, distribution, statement, K-1/tax support, amendment, restatement | `Reporting` package builder and delivery evidence | Delivered, held, amended, or restated package; request response | Stakeholder Delivery / Restatement Packet |

There is **no separate root Governance workspace**: compliance work runs through `Settings` access
review, the audit timeline, the evidence vault, and compliance-filtered queues.

### 4.4 Reusable Evidence Packets

Evidence packets are durable, versioned, permission-scoped product objects, reconstructable from
the Operational Evidence Graph — never ad hoc attachments.

| Evidence packet | Proves | Owner → approver | Blocks when absent |
| --- | --- | --- | --- |
| Import Run Evidence Contract | An import is complete, mapped, validated, lineage-safe, replayable | Data Operations → Operations Manager / domain owner | Certified datasets, reconciliation, close, NAV, report packages, delivery |
| NAV Readiness Packet | A fund/book/period can support NAV, statements, administrator tie-out, close sign-off | Fund Accountant → Controller / CFO | Period lock, statements, report packages, tax/K-1, audit, restatement release |
| Executive Financial Control Brief | Decision-grade view of cash, exceptions, close, reports, blocked outputs | CFO (evidence supplied by all operator lanes) | Board/investor packages, sign-offs, payment authorization, restatement decisions |
| Audit Event Catalog | Standardized capture of every auditable action with before/after state and correlation | Compliance (schema) / domain owners (emission) | Access certification, audit responses, legal holds, manifest freezes |
| Scoped Access Review Packet | Permissions are scoped, justified, reviewed, revoked, SoD-aligned | Security Administrator → Compliance + domain owner | Journal approval, report release, delivery, payment approval, admin actions |
| Portfolio Daily Review Packet | Daily PM control review without granting accounting authority | Portfolio Manager (evidence from Risk, Accounting, Data Ops) | Commentary, risk-escalation closure, valuation acceptance, unresolved-position sign-off |
| Stakeholder Delivery / Restatement Packet | Governed delivery, entitlement, contents, amendments, restatements | Reporting Analyst → Controller / CFO; Compliance reviews entitlement | Publication, statement release, amendments, restatements, audit/tax responses |

### 4.5 Operating Ownership (RACI)

| Object or workflow | Responsible | Accountable approver |
| --- | --- | --- |
| Journals and reversals | Fund / Investment Accountant | Controller; CFO for material or late-close items |
| NAV support and period close | Fund Accountant | Controller or CFO |
| Report packages | Reporting Analyst | Controller or CFO |
| Scoped access and entitlement | Security Administrator | Compliance Officer plus domain owner |
| Stakeholder delivery | Reporting Analyst | Controller or CFO |
| Amendments and restatements | Reporting Analyst / Fund Accountant | Controller or CFO |
| Payment request and cash evidence | Treasury Operations Specialist | CFO or configured payment approver |
| Import run certification | Data Operations Analyst | Operations Manager or affected domain owner |

Consulted and informed parties follow the owning workflow's evidence packet; auditors and
stakeholders are informed through governed delivery only.

---

## 5. Current Implementation Baseline

This design document is not a greenfield specification. Meridian has a large, tested foundation
that this charter directs toward higher value; the strategy (Section 2) is to activate and prove
it, not to widen it.

### 5.1 Evidence Sources

Implementation claims in this section are grounded in:

* `docs/roadmap/data/*.yml` and `docs/roadmap/generated/ROADMAP_SUMMARY.md` (registry snapshot 2026-08-03),
* `docs/source/data/source-modules.yml` and registered `src/**/README.md` files,
* `docs/architecture/module-map.md` and `docs/architecture/project-structure.md`,
* repository measurements taken 2026-07-28 (project, route, and test counts below).

Roadmap acceptance is bounded to the named capability. It does not by itself certify any deployment
profile or production release. Production readiness is currently **blocked**; the release gate is
every P0 row in `docs/product/implementation-todo-list.md` complete on the same release commit.

### 5.2 Scale of the Existing Foundation

Measured from the repository on 2026-07-28:

| Dimension | Measure |
| --- | --- |
| Source projects | 41 under `src/` (52 projects in `Meridian.sln` including tests and benchmarks) |
| Application code | ~242,000 lines of C# (2,902 files); 12,736 lines of F# across four deterministic calculation projects |
| Browser workstation | ~295,000 lines of TypeScript/TSX (720 files; 165 screen modules; 264 test files) |
| Desktop workstation | 497 C# files and 147 XAML views (`src/Meridian.Wpf/`) |
| API surface | 845 distinct `/api/...` routes (855 route constants in `UiApiRoutes.cs`; 1,164 endpoint mappings across 137 endpoint files) |
| Provider integrations | 22 provider adapter families under `src/Meridian.Infrastructure/Adapters/` |
| Statement connectors | 6 formats in source: declarative CSV, OFX, IB Flex XML, Alpaca, BAI2, CAMT.053 |
| Ledger library | 142 classes in `src/Meridian.Ledger/` (journals, multi-currency, capital accounts, waterfalls, tax lots, close, pricing, report packs) |
| Tests | ~12,900 xUnit facts/theories across 12 .NET test projects plus 264 browser test files |
| Durability | Write-ahead log, atomic file writes (110 call sites), checksum and audit-chain services in `src/Meridian.Storage/` |

The breadth is an asset only insofar as it is activated (Section 2.1): parts of this foundation are
not yet the wired operator path, and the W9 slate exists precisely to close that gap.

### 5.3 Wave Posture

**Closed baselines** (registry status `done`, evidence complete). W1-W5 form the accepted
operational record baseline:

| Wave / ID | Capability baseline |
| --- | --- |
| W1 `W1-DATA-001` | Provider trust gate and data confidence baseline |
| W2 `W2-TRD-001`, `W2-PROMO-001` | Paper trading cockpit reliability; paper promotion evidence and operator acceptance |
| W3 `W3-CONT-001` | Research-to-paper continuity |
| W4 `W4-RECON-001`, `W4-RPT-001` | Portfolio ledger reconciliation readiness; governed report pack readiness |
| W5 `W5-ACCT-001`, `W5-MASSET-001` | Accounting records and operational evidence; multi-asset operational coverage proof lane |
| W5X `W5X-FREX-001` | Shared Financial Record Explorers (Ledger, Portfolio, Security & Instrument, Report-Line Provenance) |
| W5X `W5X-FINOPS-001` | Financial Operations control center (Operations Continuity, close readiness, approval policy, close calendar, breaks, checklists, audit evidence, governed reopen) |
| W5X `W5X-CONNECT-001` | Statement connector library with preview, confidence, drift detection, idempotency, retained source evidence |
| W5X `W5X-EVIDENCE-001`, `W5X-STMT-ONBOARD-001` | Bounded browser-first Evidence Vault productization and statement reconciliation onboarding, including production-authority projection into queryable Statement evidence |
| W6 `W6-BTSTUDIO-001` | Bounded governed evidence loop on the host-composed browser Covered Call path: scoped retained Evidence Vault authority before queueing, exact strategy-run lineage, governed Backtest-to-Paper promotion, and four fail-closed checklist projections backed by durable operator/audit authority and a matching same-scope Paper child; broader Studio UX remains deferred |
| W7 `W7-LIVE-001` | Bounded live-readiness governance gate: paper-to-live promotion requires the full evidence set plus a manual override; broader live execution productization remains outside this completion claim |
| W9 `W9-ASSET-010` | Asset Accounting Event Spine: one governed event spine from acquisition to disposal; Expected/Projected/Drafted/Approved/Posted/Reconciled/Reported as distinct states; lot creation and versioned selected-lot disposal joined to the immutable journal in one idempotent, serializable transaction |

**Active** (registry status `in_progress`):

| ID | Capability |
| --- | --- |
| `W8-WPF-PARITY-001` | WPF desktop workstation web-UI parity over shared contracts (`docs/development/wpf-web-ui-alignment-plan.md`) |
| `W8-UX-CONSOL-001` | Browser workstation screen consolidation behind the seven charter roots (retired routes remain redirects) |

**Planned** (registry status `planned`): `W5X-OEG-001` (Operational Evidence Graph product
surface) and the ranked W9 slate:

| Rank | ID | Improvement |
| --- | --- | --- |
| 1 | `W9-TRUTH-001` | Loud, fail-closed handling of simulated data and in-memory persistence |
| 2 | `W9-DEMO-002` | One-command seeded demo on durable storage |
| 3 | `W9-PAPER-003` | Paper-trading realism: limit/stop matching and trading costs |
| 4 | `W9-ALPACA-004` | Broker fill streaming into order and ledger state |
| 5 | `W9-REPORT-005` | Client-grade PDF/XLSX exports and partners-capital statement |
| 6 | `W9-NAV-006` | Unitized NAV and real fee, waterfall, and capital-call economics |
| 7 | `W9-SAFETY-007` | Kill-switch cancel-all; fat-finger, notional, and collar rules |
| 8 | `W9-GOV-008` | Route-level authorization, fail-closed tenancy, hash-chained accounting audit |
| 9 | `W9-INGEST-009` | Institutional file ingestion (CAMT.053/BAI2) and the sided reconciliation matcher on the live path |

The W9 ordering is strategy, not backlog trivia: truth before demonstration, honest gates before
surface, deliverables before breadth, safety and governance never overpromised, trusted intake
widened last.

### 5.4 Active Product Surfaces

Two co-equal operator UI lanes run over one shared seam:

* `src/Meridian.Ui/dashboard/` — the browser workstation (built assets served from
  `src/Meridian.Ui/wwwroot/workstation/`).
* `src/Meridian.Wpf/` — the active WPF desktop workstation, projecting the same seven canonical
  workspaces; its current focus is closing web-UI parity gaps (`W8-WPF-PARITY-001`).
* `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Contracts/` provide the
  shared endpoint, read-model, and DTO seam. Both workstations consume the same product state;
  neither forks it. Presentation can differ; business state cannot.
* `src/Meridian.Ledger/` and `src/Meridian.Reporting/` are the accounting and reporting backbone:
  posted fund-event reconstruction, capital-account subledger impact, governed report-pack
  generation and delivery, report-writer grids, saved filters, formulas, and lineage.
* Direct private-capital review routes under `/api/ledger/private-capital/...` are operational
  review surfaces — not a broad LP portal, payment execution, or mobile product lane.

Visible root navigation is exactly:

```text
Trading
Portfolio
Accounting
Reporting
Strategy
Data
Settings
```

`Research`, `Data Operations`, and `Governance` remain compatibility groupings or internal route
concepts, not visible root workspaces. Meridian has **no mobile development lane**: no native
iOS/Android, MAUI, React Native, Flutter, or mobile-first workflows. Responsive browser behavior is
allowed only to keep the browser workstation usable.

### 5.5 Capability Posture by Domain

"Implemented evidence" means the repository has accepted evidence for the capability baseline.
"Supported foundation" means models, routes, services, or workflow concepts exist but the operator
product is not complete. "Planned productization" must never be presented as shipped.

| Domain | Posture | Foundation |
| --- | --- | --- |
| Data & Integration | Implemented evidence | Provider SDK, 22 adapter families, provider validation, credential/setup flows, data-confidence gates |
| Financial Operations | Implemented evidence | Reconciliation, casework, accounting close, evidence routing, NAV-support posture, fund-event accounting records |
| Treasury & Payments | Supported foundation | Cash-flow views, payment-intent design, account/ledger seams; live payment execution remains later productization |
| Portfolio & Investment Operations | Implemented evidence | Portfolio, fund-structure, brokerage sync, positions, paper sessions, valuation evidence, ledger-backed workflows |
| Reference Data | Implemented evidence | Security Master contracts, provider-to-security mapping, asset profiles, trust/conflict summaries |
| Instrument, Contract & Obligation Management | Implemented evidence | Security Master, direct-lending and F# rule kernels, factor/corporate-action evidence, multi-asset blockers |
| Entity & Relationship Management | Supported foundation | Fund-structure setup, ownership graph, legal entities, vehicles, assignments |
| Alternative Asset Management | Supported foundation | Private-credit models, governed custom asset profiles, structured/private asset coverage |
| Financing & Capital Structure Analysis | Design-led foundation | Partial support through fund, vehicle, account, and ledger models |
| Planning, Forecasting & Decision Support | Design-led foundation | Strategy, run comparison, and reporting evidence exist; engines remain future work |
| Research & Analytics | Implemented evidence | Strategy lifecycle, QuantScript, realistic backtesting runtime, promotion evidence, and the bounded `W6-BTSTUDIO-001` Covered Call scoped-Vault-to-governed-Paper evidence loop; broader Studio UX remains deferred |
| Risk Management | Supported foundation | Pre-trade risk rules, live-readiness controls; enterprise risk remains expansion work |
| Client & Stakeholder Reporting | Implemented evidence | Governed report packs, provenance, export evidence, publication/restatement lifecycle |
| Collaboration & Communication | Design-led foundation | Workflow assignment, comments, audit events, queue state |
| Administration & Governance | Implemented evidence | Settings, policy, provider setup, audit trail, approval controls, governed stage gates |
| Audit, Compliance & Regulatory | Implemented evidence | Audit events, evidence manifests, report provenance, approval history, controlled close/report workflows |
| Workflow & Process Automation | Supported foundation | Shared workflow DTOs, operator queues, lifecycle transitions, acceptance gates |
| Document & Knowledge Management | Implemented evidence | Evidence Vault identity/intake/query/review/manifest/audit baseline and browser-first statement onboarding are closed; broader document portal and collaboration remain deferred |
| Reporting & Analytics Platform | Implemented evidence | Report-pack workflow, line provenance, trial-balance reporting, export evidence; client-grade rendering activation tracked as `W9-REPORT-005` |

---

## 6. Functional Domain Catalog

Domains describe business capability areas, not services or database schemas. Each domain lists its
purpose, its core capabilities, and the operating requirements that survive any implementation.
Ownership boundaries live in the bounded context map (Section 7).

### 6.1 Data & Integration

Acquire, normalize, validate, and distribute provider, file, and external-system data while
retaining source evidence, confidence posture, and replayable lineage.

Core flow: `Connect Source → Acquire → Validate → Normalize → Store → Publish`.

Capabilities: provider catalog and readiness posture; onboarding and secret-safe credential
validation; API ingestion for market, brokerage, bank, accounting-system, and reference data; batch
and document intake (SFTP, upload, governed email capture); raw source capture with hashes,
duplicate keys, and provenance; mapping into canonical positions, transactions, balances, and
reference records; validation, freshness checks, and confidence scoring; replay-safe import jobs;
lineage from source payload to report line; import replay and backfill with prior-version
preservation; shared publication into storage, accounting, reporting, strategy, and both
workstations.

Operating requirements: UI surfaces consume shared contracts for provider posture rather than
owning provider-trust logic; missing or stale evidence creates `review-required`/`blocked` states;
new provider work starts from ProviderSdk contracts and Infrastructure adapters, publishes through
shared services, then adds browser presentation, then WPF presentation from the same services.

### 6.2 Financial Operations

Reconciliation, exception management, accounting operations, close support, workflow control, and
audit evidence. Core flow: `Receive Activity → Match → Resolve Exceptions → Approve → Produce
Evidence`.

Capabilities: cash, position, trade, income, MBS-factor, bank, and GL reconciliation; exception
management with assignment and escalation; close checklists and calendars; fund-event accounting
support; partner capital account tie-outs; shadow NAV and NAV-support packages; expense, fee, and
allocation review; period close locks and reopen evidence; operational dashboards; evidence
packages; approval history.

Every exception is a governed operating case carrying: owner, queue, SLA due date, severity,
materiality, root cause, source system, affected fund/book/period, blocked outputs, and evidence
status. Queues expose new, assigned, waiting-on-evidence, waiting-on-approval, resolved, reopened,
and waived views. Escalation is automatic and auditable when an exception blocks close, journal
posting, report approval, or package delivery. Waivers require permissioned approval, materiality
rationale, and retained evidence; reopened exceptions preserve prior resolution history.

The completed `W5X-FINOPS-001` control center is the shared Accounting/Reporting command surface:
close and reconciliation state, priority-ranked exception queues, release safety, and mandatory
drill-through to the Ledger Explorer, Evidence Vault, and Report-Line Provenance Explorer. The Fund
Event Command Center (create, review, reconcile, approve, report, and lock fund events end to end)
remains a **planned** roadmap candidate and must not be described as implemented until registry
acceptance exists.

### 6.3 Treasury & Payments

Record and prove money-movement intent: payment requests, liquidity, expected cash movement, bank
evidence, reconciliation, and capital activity. Capabilities: payment request records with
independent approval separation, expected cash movement projection, bank confirmation capture,
return/reversal evidence, reconciliation handoff, audit linkage. Live payment execution, bank
release automation, and bill pay remain later productization; no workflow copy may imply Meridian
has released money until a roadmap row implements live execution.

### 6.4 Portfolio & Investment Operations

Holdings, positions, transactions, exposures, valuations, and investment activity: brokerage and
custodian sync, fund accounts, position blotters, lot-level cost basis, corporate-action effects,
valuation evidence, paper-session records, and ledger-backed portfolio workflows.

### 6.5 Reference Data

Authoritative identifiers, classifications, currencies, calendars, ratings, and taxonomies,
including Security Master projections. Reference Data does not own instrument contract terms or
cash-flow logic.

### 6.6 Instrument, Contract & Obligation Management

The financial engine: authoritative source for instruments, contract terms, obligations, rights,
schedules, lifecycle events, and expected cash flows. Core principle:

```text
Instrument = what it is
Contract = the terms
Obligation = what must happen
Expected Cash Flow = what should happen
Transaction = what actually happened
Reconciliation = did they agree
```

Supported financial objects span public securities, fixed income, loans and direct lending, leases,
derivatives, structured products, private fund interests, real assets, deposits, guarantees, and
commitments — recorded widely, automated narrowly, per contract packs (Section 22).

### 6.7 Entity & Relationship Management

People, organizations, trusts, funds, SPVs, beneficiaries, and their ownership and authority
relationships: relationship graph, ownership percentages, authorized signers, KYC/AML posture
references, and effective-dated assignment history.

### 6.8 Alternative Asset Management

Assets beyond public securities: real estate, private credit, private equity, structured products,
and fund interests, with look-through, capital commitments, and waterfall modeling support. The v1
baseline keeps `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`,
`RealEstateHolding`, `CommitmentGuarantee`, and `DirectLoan` classes first-class in the Security
Master; live vertical-system adapters remain deferred.

### 6.9 Financing & Capital Structure Analysis

Evaluation and monitoring of financing, leverage, and capital structures: debt facilities,
subscription and NAV lines, repo and securities lending, covenants, collateral, borrowing base, and
leverage analysis. Design-led; activation requires a roadmap row (Section 24).

### 6.10 Planning, Forecasting & Decision Support

Forward-looking cash, liquidity, revenue, distribution, debt, and expense forecasting with
scenarios, assumption sets, and decision records. Forecast inputs are retained as evidence and
linked to cash, budget, close, and report objects. Engines remain future work.

### 6.11 Research & Analytics

Investment research and strategy development: notes, watchlists, screening, QuantScript notebooks,
backtesting with realistic fill and cost models, walk-forward analysis, paper portfolios, strategy
run comparison, and promotion evidence. The bounded W6 path starts from the browser Covered Call
form, applies count/value/aggregate budgets, resolves a strict canonical retained Evidence Vault
manifest inside the authenticated tenant/company scope, records the pre-execution entry through the
shared strategy-run repository, and preserves exact scope and lineage through replay, review, and
Trading readiness.

Operator acceptance text remains a requirement until the governed Backtest-to-Paper promotion
records a durable approved decision with operator, time, audit authority, every canonical Paper
checklist id, keyed evidence exactly matching the source run, and an exact same-scope Paper child
whose parent and strategy identities match. Missing, rejected, foreign, or mismatched evidence or
lineage remains review-required or rejected; metric eligibility and generic paper-session creation
cannot satisfy the checklist.
`BacktestStudioRunOrchestrator` is not host-composed, and the Strategy Designer currently fails
closed when its production compiler captures no result, so neither is evidence for W6 closure.

### 6.12 Risk Management

Pre-trade order safety (position limits, order-rate throttles, drawdown circuit breakers),
live-readiness controls, exposure and concentration signals, breach states with explicit
acknowledgement, and risk reporting. PM acknowledgements cannot approve accounting records,
override compliance policy, or close reconciliation blockers. The enterprise risk platform remains
expansion work.

### 6.13 Client & Stakeholder Reporting

Deliver information to clients, beneficiaries, LPs, trustees, boards, and committees: governed
packages with explicit states, delivery blockers, amendment and restatement lifecycles that never
overwrite the original, and entitlement-limited stakeholder views (Section 18).

### 6.14 Collaboration & Communication

Coordination between operators, reviewers, managers, and external parties: comments, assignments,
waiting-on states, escalation history, and durable audit retention. Collaboration cannot
substitute for domain approval: journal, report, access, payment, and close decisions use their
owning approval workflow.

### 6.15 Administration & Governance

Platform configuration, security, roles, rules, and governance: organization, entity, portfolio,
account, book, period, report, and delivery admin scopes; journal/report/period-lock/export
permissions; onboarding and cloning templates; immutable admin logs for every posting, lock,
export, and delivery event.

### 6.16 Audit, Compliance & Regulatory

Evidence, controls, retention, and audit support: the Audit Event Catalog schema (actor, action,
object, scope, before/after state, reason, approval reference, retention class, legal-hold state,
correlation ID), access certification states, legal holds that override disposal and remain visible
across packet, package, delivery, and audit workflows, and compliance blockers.

### 6.17 Workflow & Process Automation

Business processes, approvals, task routing, review queues, and recurring workflows. Automation may
draft, classify, match, summarize, or flag — it cannot approve its own work, post material
journals, override period locks, release payments, publish reports, or erase evidence
(Section 21). Workflow templates are domain-aware; generic process configuration cannot own
reconciliation, payment, report, journal, or access-control truth.

### 6.18 Document & Knowledge Management

Documents, attachments, evidence, extracted metadata, and search: versioning, OCR/text extraction,
retention policies, permission-aware access, and evidence links (Section 19).

### 6.19 Reporting & Analytics Platform

Shared analytics and reporting infrastructure: certified operational datasets and data marts with
row-level lineage, refresh cadence metadata, staleness disclosure, drill-through, export approval,
and the rule that nothing is marked certified while upstream import, reconciliation, journal,
access, or package approval states are incomplete.

---

## 7. Bounded Context Map

Functional domains describe capabilities; bounded contexts define ownership of data, rules, and
language.

### 7.1 Core Bounded Contexts

| Bounded context | Owns |
| --- | --- |
| Identity & Access | Users, roles, permissions, tenant access, authentication, authorization |
| Entity & Relationship | Legal entities, people, trusts, funds, SPVs, beneficiaries, ownership relationships |
| Data Provider & Integration | Providers, connections, credential references, import jobs, source metadata, file/API ingestion, provider health |
| Reference Data | Identifiers, classifications, currencies, calendars, ratings, taxonomies |
| Instrument, Contract & Obligation | Financial instruments, contract terms, schedules, obligations, lifecycle events, expected cash flows |
| Portfolio Records | Holdings, positions, transactions, lots, cost basis, income activity, corporate actions |
| Financial Operations | Reconciliations, breaks, exceptions, operational reviews, adjustments, close checklists, evidence packages |
| Treasury & Payments | Bank accounts, cash balances, payment requests, ACH, wires, capital calls, distributions, payment approvals |
| Alternative Assets | Real estate, private credit, private equity, structured assets, valuation inputs, asset-level cash flows |
| Financing & Capital Structure | Debt facilities, synthetic financing, loan agreements, capital stacks, covenants, collateral terms, leverage analysis |
| Planning & Forecasting | Forecast models, scenarios, assumptions, stress tests, planning cases, decision records |
| Research & Analytics | Research notes, watchlists, investment theses, backtests, strategy runs, analytical workspaces |
| Risk Management | Risk rules, metrics, limits, exposure calculations, concentration checks, breaches |
| Workflow & Task | Tasks, assignments, statuses, approvals, escalations, SLA tracking |
| Audit & Compliance | Audit events, control evidence, approval history, compliance checks, retention policies |
| Reporting & Client Delivery | Reports, dashboards, statements, client packages, investor packages, delivery history |
| Document & Knowledge | Documents, attachments, versions, metadata, extracted text, evidence links, search index |

### 7.2 Removed Context

AI & Automation was removed as a standalone business domain. AI is a cross-cutting implementation
capability governed by Section 21; it is not part of the formal domain model.

---

## 8. Context Ownership Matrix

| Bounded context | Owns data | Owns rules | Exposes APIs | Has UI | MVP |
| --- | --: | --: | --: | --: | --: |
| Identity & Access | Yes | Yes | Yes | Yes | Yes |
| Entity & Relationship | Yes | Yes | Yes | Yes | Yes |
| Data Provider & Integration | Yes | Yes | Yes | Yes | Yes |
| Reference Data | Yes | Yes | Yes | Yes | Yes |
| Instrument, Contract & Obligation | Yes | Yes | Yes | Yes | Yes / Limited |
| Portfolio Records | Yes | Yes | Yes | Yes | Yes |
| Financial Operations | Yes | Yes | Yes | Yes | Yes |
| Treasury & Payments | Yes | Yes | Yes | Yes | Later |
| Alternative Assets | Yes | Yes | Yes | Yes | Later |
| Financing & Capital Structure | Yes | Yes | Yes | Yes | Later |
| Planning & Forecasting | Yes | Yes | Yes | Yes | Later |
| Research & Analytics | Yes | Yes | Yes | Yes | Later |
| Risk Management | Yes | Yes | Yes | Yes | Later |
| Workflow & Task | Yes | Yes | Yes | Yes | Yes |
| Audit & Compliance | Yes | Yes | Yes | Yes | Yes |
| Reporting & Client Delivery | Partial | Yes | Yes | Yes | Yes |
| Document & Knowledge | Yes | Yes | Yes | Yes | Yes |

---

## 9. Recommended MVP Contexts

The first build focuses on the operational foundation.

### 9.1 MVP Contexts

```text
1. Identity & Access
2. Entity & Relationship
3. Data Provider & Integration
4. Reference Data
5. Instrument, Contract & Obligation — limited version
6. Portfolio Records
7. Financial Operations
8. Workflow & Task
9. Audit & Compliance
10. Reporting & Client Delivery
11. Document & Knowledge
```

### 9.2 Later Expansion Contexts

```text
1. Treasury & Payments
2. Alternative Assets
3. Financing & Capital Structure
4. Planning & Forecasting
5. Research & Analytics
6. Risk Management
```

### 9.3 MVP Screen Inventory

The MVP operator surface maps to the seven root workspaces. Screen consolidation
(`W8-UX-CONSOL-001`, decision `DEC-UI-CONSOLIDATION-001`) folds standalone screens into deeper host
screens behind these roots; retired routes remain redirects.

| Screen | Workspace | Bounded context |
| --- | --- | --- |
| Home Dashboard | Trading | Platform Runtime |
| Provider Center | Data | Data Provider & Integration |
| Import Runs | Data | Data Provider & Integration |
| Data Quality | Data | Data Provider & Integration |
| Entity Directory | Accounting | Entity & Relationship |
| Account Directory | Portfolio | Portfolio Records |
| Instrument / Contract Registry | Accounting | Instrument, Contract & Obligation |
| Portfolio Records | Portfolio | Portfolio Records |
| Reconciliation Workbench | Accounting | Financial Operations |
| Exception Queue | Accounting | Financial Operations |
| Workflow Tasks | Accounting | Workflow & Task |
| Audit Log | Reporting | Audit & Compliance |
| Document Vault | Reporting | Document & Knowledge |
| Reporting Packages | Reporting | Reporting & Client Delivery |
| Administration Settings | Settings | Identity & Access |

---

## 10. Core Business Object Model

### 10.1 Core Object Hierarchy

```text
Tenant / Organization
        ↓
Entity
        ↓
Account / Capital Account / Ledger Account
        ↓
Position / Contract / Obligation
        ↓
Expected Cash Flow
        ↓
Actual Transaction / Actual Cash Flow / Journal Entry
        ↓
Reconciliation / Reporting / Audit
```

### 10.2 Core Objects

| Object | Purpose | Examples |
| --- | --- | --- |
| Tenant | Customer environment | Fund administrator, RIA, family office |
| Entity | Legal or economic party | Fund, trust, LLC, individual, SPV |
| Relationship | Link between entities | Owner, beneficiary, advisor, custodian, lender, borrower |
| Account | Container where assets, cash, or activity live | Bank account, custody account, investment account, GL account |
| Capital Account | Economic record for investor or owner activity | Commitment, contribution, distribution, allocation, NAV share |
| Ledger Account | Accounting account used for balanced postings | Cash, receivable, payable, income, expense, capital account |
| Instrument | Defines what something is | Bond, stock, loan, lease, swap, real estate asset |
| Contract | Defines rights and obligations | Loan agreement, bond indenture, lease, credit facility |
| Obligation | Future duty or right to pay or receive | Coupon, principal, rent, capital call, distribution |
| Expected Cash Flow | Forecasted cash movement from terms | Scheduled interest, maturity payment, rent payment |
| Fund Event | Operational event requiring accounting evidence | Closing, investment, capital call, distribution, expense |
| Transaction | Actual observed activity | Trade, wire, coupon receipt, journal entry |
| Journal Entry | Balanced accounting record owned by Meridian | Accrual, valuation adjustment, cash receipt, capital activity |
| Position | Ownership or exposure at a point in time | Shares, par value, LP interest, loan balance |
| Valuation | Value assigned to an object | Market value, NAV, appraisal, fair value |
| Reconciliation | Comparison between sources | Custodian vs internal, bank vs ledger, expected vs actual |
| Exception | Difference requiring resolution | Missing trade, price break, cash variance |
| Document | Supporting evidence | Statement, invoice, confirmation, agreement |
| Task | Work assigned to a user | Review break, approve payment, validate import |
| Report Package | Final output for review/distribution | Investor report, audit package, board packet |
| Delivery Record | Evidence of stakeholder publication | Recipient list, timestamp, channel, package version |
| Audit Event | Immutable history of meaningful actions | Approved recon, changed terms, imported file |

Fund events are fund/private-capital specializations of the broader operational-event spine that
connects evidence, workflow, treasury, ledger, capital accounts, reconciliation, reporting,
delivery, tax, and audit impact. New durable business nouns require a `docs/domain/` dictionary
page before broad code generation.

### 10.3 Object Relationship Model

```text
Tenant
 └── Entity
      ├── Account
      │    ├── Position
      │    │    └── Instrument
      │    ├── Transaction
      │    └── Cash Balance
      │
      ├── Contract
      │    ├── Instrument
      │    ├── Obligation
      │    └── Expected Cash Flow
      │
      ├── Document
      └── Relationship
```

---

## 11. System of Record Strategy

### 11.1 Core Principle

| Concept | Meaning |
| --- | --- |
| Source of Data | Where information came from |
| Source of Truth | The trusted source for a specific field or purpose |
| System of Record | The approved internal record after validation, override, and approval |

### 11.2 Source Priority Model

Source hierarchy rules are configurable per tenant:

| Data type | Primary source | Secondary source | Override allowed |
| --- | --- | --- | --- |
| Bank Cash | Bank feed | Custodian | Yes |
| Custody Positions | Custodian | Investment accounting system | Yes |
| Security Terms | Approved data vendor | Custodian | Yes |
| Prices | Approved pricing vendor | Broker quote | Yes |
| Transactions | Custodian | Internal import | Yes |
| Accounting Entries | Meridian ledger | GL / accounting-system export | Via approved reversing or adjusting journal |
| Contract Terms | Executed agreement | Data vendor | Yes |
| Entity Ownership | Legal documents | Internal admin | Yes |

### 11.3 Source-of-Record Layers

1. **External raw sources** — capture exactly what was received (custodian files, bank feeds,
   broker records, vendor files, GL exports, PDFs, contracts, statements). Raw source data is
   preserved exactly as received before any transformation.
2. **Normalized operational records** — converted into Meridian's standard model (transactions,
   positions, cash balances, prices, contract terms).
3. **Validated business records** — validation, mapping, exception checks, and business rules
   applied. Invalid mapped records never silently enter the canonical store; they are quarantined.
4. **Approved system of record** — Meridian's official internal record for operations, reporting,
   reconciliation, forecasting, and audit.

### 11.4 Lineage and Provenance

Every system-of-record value carries lineage:

```text
Source system
Source file/API
Import run
Received timestamp
Normalized timestamp
Validation status
Approval status
Approved by
Approved timestamp
Override flag
Override reason
Supporting document
```

Truth discipline (Section 2.3) extends lineage with **per-datum provenance**: a value derived from
synthetic, fixture, backfilled, or estimated inputs carries that marking wherever reconciliation,
NAV, certification, or reporting consumes it — provenance at page level only is insufficient
(`W9-TRUTH-001`).

### 11.5 Override Strategy

| Rule | Requirement |
| --- | --- |
| Preserve original value | Always |
| Require reason | Always |
| Create audit event | Always |
| Require approval | Based on materiality |
| Review or expire override | For prices, ratings, assumptions, temporary corrections |

### 11.6 Reconciliation Strategy

Three reconciliation dimensions:

| Type | Example |
| --- | --- |
| Source-to-Source | Custodian position vs administrator position |
| Expected-to-Actual | Expected coupon vs actual coupon received |
| Internal-to-Official | Meridian record vs general ledger |

Reconciliation is a **sided** comparison — statement side versus ledger side — never a self-check
of one side against itself. Matching prefers strong identifiers (internal ID, CUSIP, ISIN, SEDOL,
FIGI, provider ID) before ticker+exchange+currency; ticker alone is not sufficient. Cross-currency
reconciliation requires an implemented FX rate source on the live path.

Reconciliation output flows into Financial Operations exception casework whenever an unmatched
item, tolerance breach, stale input, missing document, disputed value, approval gap, or
ledger/report variance can affect close, posting, report approval, delivery, NAV support, capital
accounts, tax support, audit evidence, or certified exports. Every exception carries the case
fields of Section 6.2 and links into the proof surfaces operators use to finish downstream work:
Evidence Vault, Ledger Explorer, Report-Line Provenance Explorer, and the Operational Evidence
Graph.

### 11.7 Treasury Ledger Principles

These invariants apply wherever records affect cash, capital accounts, accounting balances, payment
workflows, or close packages:

| Principle | Meridian requirement |
| --- | --- |
| Double-entry | Every balance-affecting journal transaction has at least one debit and one credit, and debits equal credits per currency. |
| Atomic write | A journal transaction's entries either all persist or all fail; callers cannot create orphan debit or credit rows. |
| Idempotency | Import runs, payment intents, bank confirmations, and journal requests carry stable source keys so retries cannot duplicate money movement or accounting entries. |
| Posted immutability | Posted entries cannot be edited or deleted; corrections use reversing or adjusting journals linked to the original record. |
| Pending lifecycle | Draft or pending journal transactions can be amended before approval, then posted, archived, or superseded with version history. |
| Versioned balances | Ledger accounts, journal transactions, close packages, and settlements expose versions so past states can be reconstructed precisely. |
| Effective dating | Entries keep effective date, posted timestamp, source timestamp, and approval timestamp separately. |
| Concurrency control | Writes that depend on balance, close status, settlement status, or approval state use optimistic version checks and fail closed on stale state. |
| Payment linkage | Payment requests, bank orders, confirmations, returns, and reversals link to ledger transactions, but payment processors do not become the source of ledger truth. |
| Audit reconstruction | Every ledger balance in a report package can drill back to entries, source records, approvals, documents, and any reversal chain. |

Hardening direction (`W9-GOV-008`): the authoritative accounting audit trail becomes hash-chained
so tampering is detectable, complementing the existing storage-layer audit chain.

---

## 12. Recommended Architecture Style

Meridian is a **modular monolith** with strict bounded-context boundaries — not microservices from
day one. Ledger posting, position changes, allocations, consolidations, official locks, and close
controls require strong transactional consistency. Separate services remain appropriate for
connector execution, document processing, workflow orchestration, search, analytics, notifications,
and AI workloads.

### 12.1 Module Structure

```text
Meridian.Platform
Meridian.Identity
Meridian.Entities
Meridian.DataIntegration
Meridian.ReferenceData
Meridian.Instruments
Meridian.PortfolioRecords
Meridian.FinancialOperations
Meridian.Workflow
Meridian.Audit
Meridian.Reporting
Meridian.Documents
```

These names are the bounded-context module target; every module has a physical project under
`src/`. Project adaptation is tracked through
[`docs/architecture/design-document-adaptation.md`](../architecture/design-document-adaptation.md),
with physical conformance in
[`docs/architecture/design-module-conformance.md`](../architecture/design-module-conformance.md).
New implementation work selects the bounded-context module first, then uses those maps to choose
the physical project, current source owner, contracts, UI surface, and validation lane.

Each module carries:

```text
Domain model
Application services
Contracts / APIs
Infrastructure
UI components
Tests
```

### 12.2 Design Rule

Other modules may read through published APIs, views, or events, but they must not directly write
another module's owned records.

---

## 13. Tenancy Strategy

Hierarchical multi-tenancy:

```text
Tenant
    ↓
Operating Organization
    ↓
Legal Entity / Client / Fund / Family Group
    ↓
Account / Portfolio / Vehicle
    ↓
Position / Contract / Transaction
```

Worked hierarchies: a fund administrator tenant scopes client fund groups → funds → investors,
vehicles, accounts, and capital accounts; an RIA tenant scopes households → clients → investment
accounts; a family office tenant scopes family branches → trusts, LLCs, and individuals → accounts,
investments, and properties.

Every core record is scoped by `TenantId`, plus `EntityId` and `AccountId` where applicable.
Permissions are filterable by tenant, entity, account, portfolio, fund, household, report package,
and document.

### Authoritative Consistency Model

Authoritative tenancy, scoped access, fund structure, approvals, ledger, reconciliation, and audit
state prefer consistency over write availability when Meridian runs multiple instances against
shared data. During store conflicts, stale assignment versions, unavailable authoritative stores,
or ambiguous scope resolution, Meridian **fails closed** rather than allowing split-brain authority
or conflicting operational state. Tenancy resolution itself fails closed: a caller without a
resolvable tenant scope receives no data, not all data (`W9-GOV-008`).

Read models, search indexes, dashboards, and market-data caches may be eventually consistent where
the workflow remains safe, but authorization decisions and governed write paths must consult the
authoritative scope model or a verified fresh cache.

---

## 14. Permission Strategy

Meridian uses role-based access control (what a user can do) and attribute-based access control
(what data they can access). A permission decision combines:

```text
Role
+ Capability
+ Data Scope
+ Approval Limit
+ Segregation of Duties Rule
```

The authorization question is always: **can principal P perform permission X on scope S at time
T?** Scoped access is recorded as governed `UserAccessAssignment` records binding principal → scope
→ role/permission mask with effective dates, grantor, rationale, correlation ID, version,
revocation evidence, and a linked audit event — maintained with optimistic concurrency and
fail-closed behavior.

Direction (`W9-GOV-008`): every mapped route carries an explicit authorization requirement;
segregation-of-duties and dual-approval checks are enforced on money-affecting paths (payment
release, break closure, material journals), not advisory.

---

## 15. Workflow Strategy

Workflows are configurable but domain-aware; Meridian deliberately avoids a fully generic,
finance-ignorant workflow engine.

```text
Workflow Template
→ Workflow Instance
→ Steps
→ Tasks
→ Assignments
→ Approvals / Rejections / Escalations
```

Ownership rule: Workflow owns tasks, assignments, statuses, approvals, escalations, and SLA
tracking. Workflow does **not** own reconciliations, payments, reports, or transactions — those
belong to their domain contexts, which expose workflow hooks.

---

## 16. Rules Strategy

Rules are versioned, testable, auditable, and effective-dated. Categories: validation, mapping,
matching, source-of-record, approval, materiality, exception, reporting, and entitlement rules.

Lifecycle: `Draft → Tested → Approved → Active → Retired`. Rules require test cases before
activation.

---

## 17. Integration Strategy

### 17.1 Provider Adapter Model

```text
Provider
→ Connection
→ Dataset
→ Import Run
→ Raw Record
→ Mapping Profile
→ Normalized Record
→ Validation Result
→ Published Business Record
```

Supported provider types: market data, brokerage, custodian, bank, accounting system, fund
administrator, pricing, reference data, document, email/SFTP intake, and manual governed input.
The integration rule: preserve raw source exactly as received, then Raw → Normalized → Validated →
Approved → Published. Mapping versions are tied to import runs; import replay preserves lineage and
prior versions.

### 17.2 Provider Integration Manifests (No-Code)

Declarative, versioned manifests separate reusable **provider templates** from tenant **connection
instances**: guided setup, visual mapping with per-field confidence, a safe transform library,
ingestion runtime with quarantine and identity resolution, scheduling, drift detection, and
activation gates. Boundaries:

* The generic no-code connector is **read-only**. Production write capabilities — order preview,
  placement, cancel/replace, cash transfer — require certified provider modules with sandbox
  validation, approvals, entitlements, idempotency, kill switch, audit, and reconciliation
  evidence. FIX and production trade-execution APIs are never activated through generic mapping.
* Arbitrary user-written pipeline code is limited to admin/developer roles.
* Schema drift pauses affected mappings until reviewed and approved.
* Security matching prefers strong identifiers before ticker heuristics (Section 11.6).
* Secrets live in credential references, never in manifest payloads.
* Production activation requires: auth test, endpoint test, sample load, required mappings,
  validation pass, dry-run sync, reconciliation review, and approval.

### 17.3 Ingestion Tiers and Standards

Connectors follow one lifecycle: authenticate, retrieve, retain the original payload, detect
duplicates, normalize, map identities, validate, reconcile, publish business events, monitor
freshness and failures. Ingestion spans direct APIs and webhooks, structured files and financial
messages (including institutional bank formats CAMT.053 and BAI2 — connectors exist in source;
live-path acceptance is `W9-INGEST-009`), documents and email capture, and governed manual input.
External standards serve as interoperability vocabulary, not database shape: FIBO terminology,
FIGI/ISIN/CUSIP identifiers, ISO 20022 cash concepts, ILPA-compatible private-market reporting.

---

## 18. Reporting Strategy

Reporting owns: templates, definitions, packages, schedules, distribution history, recipient lists,
rendered outputs, approval status, certified dataset definitions, and stakeholder delivery records.
Reporting consumes approved records from owning contexts; it does **not** own source financial
data.

Published report rule: published reports are **frozen snapshots**. Each package knows the data it
used, generation time, template version, approver, recipients, amendment status, and its supporting
certified dataset and evidence manifest. Neither amendment nor restatement overwrites the original;
superseded, current, pending-amendment, and restated states are visible side by side.

Client-grade output requirement (`W9-REPORT-005`): governed exports must be client-presentable —
typed spreadsheet cells that calculate, layout-quality PDF, full Unicode entity and investor names,
and a partners-capital statement computed per investor from ledger-backed records rather than
name heuristics. Deliverables must leave Meridian ready to send, not ready to re-type.

Delivery direction (Section 2.4): every delivered package exposes governed, entitlement-scoped
drill-through from report line to evidence for its recipients. The broad self-service portal
remains deferred.

---

## 19. Document and Evidence Strategy

The Document & Knowledge context owns document records; documents link across Entity, Account,
Instrument, Contract, Transaction, Position, Reconciliation, Exception, Journal Entry, Capital
Account, Report Package, Delivery Record, and Audit Event. Required metadata includes hash,
retention class, confidentiality class, evidence tag, access policy, and approval status.

The **Evidence Vault** (`W5X-EVIDENCE-001`, completed as a bounded browser-first baseline) productizes retained-document identity, intake,
request lists by event/close/audit/tax/report package, document lists, extracted-field review with
confidence and reviewer state, object links, immutable manifests, and audit state as a reusable
shared evidence layer. Statement onboarding (`W5X-STMT-ONBOARD-001`, completed) connects browser-first
statement import to committed reconciliation with retained vault proof, including an authority-verified
Statement projection in production composition. Document extraction becomes
accounting-grade evidence only after it is validated, linked to events or journals, reviewed, and
frozen into close, tax, audit, or reporting manifests. Legal holds override disposal and remain
visible wherever the held evidence appears.

---

## 20. Extensibility and Configuration Strategy

A stable financial operations core with governed configuration layered around it:

```text
Stable Core
+ Configurable Business Rules
+ Configurable Workflows
+ Pluggable Integrations
+ Extensible Domain Models
+ Tenant-Specific Templates
```

Engineering boundaries live in
[`docs/architecture/core-extensibility-model.md`](../architecture/core-extensibility-model.md); the
shared contract seam is `src/Meridian.Contracts/Extensibility/`.

**Stable core objects** (consistent across tenants): Tenant, Entity, Relationship, Account,
Instrument, Contract, Obligation, Expected Cash Flow, Transaction, Position, Valuation,
Reconciliation, Exception, Capital Account, Ledger Account, Journal Entry, Fund Event, Document,
Task, Report Package, Audit Event.

**Configurable areas**: workflows, rules, integrations and mapping profiles, report templates and
schedules, permissions and scopes, classifications, custom fields, source priority, ledger controls
(posting rules, idempotency keys, period locks, reversal policy), and notifications.

**Not configurable** (governed and consistent everywhere): audit trail, security model foundation,
core object identity, financial calculation integrity, data lineage model, approval evidence model,
immutable record preservation.

Configuration lifecycle: `Draft → Tested → Reviewed → Approved → Active → Superseded → Retired`.
Every configuration record carries standard metadata (ID, type, owning context, version, effective
dates, approver, linked audit event, rollback version, and scope). Scope levels: Global, Tenant,
Entity Group, Entity, Account, Workflow Instance, User.

Configuration risk controls (the guardrails that keep configurability safe):

| Risk | Control |
| --- | --- |
| Core drift | Core financial objects stay stable; extensions use governed custom fields and packs |
| Unreviewed change | Configuration changes require approval and effective dates |
| Mapping drift | Mapping versions are tied to import runs; drift pauses sync |
| Report mutation | Published packages are frozen; changes create new versions |
| Duplicate money movement | Idempotency keys and source-event uniqueness are mandatory |
| Stale-state writes | Balance-sensitive writes use version checks and fail closed |
| History tampering | Posted history corrects only via reversing/adjusting journals |
| Concentration of authority | Segregation of duties enforced by permissions and workflow |
| Replay corruption | Import replay preserves lineage and prior versions |
| Untested rules | Rules require test cases before activation |
| Secret leakage | Secrets live in credential references, never in payloads or manifests |

---

## 21. Governed Autonomy and Responsible AI

Meridian's operating leverage comes from automation that works **inside** deterministic controls.
AI operates above deterministic financial services and below human authority.

**Automation and AI may:** extract terms, propose entity or ownership relationships, suggest
mappings, match transactions, explain reconciliation differences, draft journals and commentary,
identify missing documents, classify exceptions, summarize evidence, and answer questions with
evidence links.

**Automation and AI must never:** post material journals; approve their own work or any downstream
workflow depending on their suggestion; override period locks; release payments; publish governed
reports; change ownership records; calculate authoritative waterfalls from free-form language;
replace deterministic tax, accounting, or performance calculations; edit posted entries; delete
evidence, manifests, source files, extracted fields, audit events, or lineage records; or answer
without exposing sources and calculation basis.

**Review states.** Every assistant suggestion passes through explicit human-controlled states —
`Suggested → Reviewed → Accepted / Edited / Rejected / Escalated` — and retains model and version,
input context, reviewer, review timestamp, source evidence references, confidence markers, human
edits, and the resulting downstream transition.

**Policy-approved straight-through lanes.** Routine throughput may move from per-item approval to
per-policy approval only under all of these conditions: the executing logic is deterministic rules,
not model output; a human-approved, versioned policy defines the eligible class with materiality
caps; every action retains full evidence and audit events and remains reversible through governed
correction; sampling review and a kill switch exist; and material or high-risk classes stay
per-item. This is how the Governed Touchless Rate (Section 1.5) rises without weakening control.

A future **Policy Compiler** converts plain-language policy into proposed structured rules with
sample calculations, edge cases, historical comparisons, explicit user approval, and versioned
production rules.

AI & Automation is not a business domain (Section 7.2); autonomous-agent workflows that bypass
operator evidence are out of scope absent a roadmap decision.

---

## 22. Target Architecture and Product Wedge

The durable architecture is a common financial core (~70% of the technology: multi-book ledger
records, positions, valuations, source vault, documents, workflows, reconciliations, reporting,
data connections, evidence graph, governed permissions) with vertical packages:

| Common platform | Fund Administration package | Family Office package |
| --- | --- | --- |
| Ledger, positions, valuations, vault, documents, workflows, reconciliations, reporting, connectors, evidence graph, permissions | Commitments, closings, allocations, equalization, waterfalls, carry, fees, partner capital accounts, NAV workflows, investor onboarding, statements, notices | Entity accounting, consolidations, eliminations, trusts, household views, intercompany activity, liquidity planning, liabilities, guarantees, advisor coordination |

The authoritative ledger remains the financial source of truth. Object storage preserves raw
payloads; an event bus distributes normalized business events; a columnar warehouse serves
analytics; a graph projection supports ownership and evidence navigation; a search index supports
discovery. The graph and warehouse are **projections, not competing books of record**.

**Contract packs.** Assets and liabilities are economic contracts, not hard-coded product
categories. The common spine defines identity, ownership, entity, book, evidence, lifecycle,
valuation, accounting, reconciliation, permission, and reporting hooks; versioned packs define
contract schema, lifecycle events, valuation methods, accounting rules, validations, and reporting
taxonomy. Launch prefers wide capture and narrow automation.

**Number Passport.** The signature evidence object for every balance, performance figure, capital
account value, report total, dashboard metric, close variance, or liquidity forecast: amount,
currency, book, basis, dates, underlying positions, journal entries, source records and documents,
extracted fields, transformation and allocation rules, valuation methodology, reconciliation state,
preparer/reviewer/approval history, period-over-period change, confidence, and freshness.

**Market-entry wedge.** The first packaged product is the **Close, Data and Evidence Control
Tower**: entity and ownership graphs, asset/liability registry, document and source-data vault,
CSV/Excel/email/SFTP ingestion, priority bank and custodian connectors, opening-balance and
position reconciliations, close checklists, reviewer workflows, exception management, consolidated
reporting, Number Passports, audit-ready close binders, and governed read-only stakeholder views.
Stage two becomes the native accounting system (multi-book ledger, private-capital accounting).
Stage three builds the ecosystem: connector SDK, certified asset packs, policy and report template
marketplace, and controlled AI agents.

The north-star metric is **Verified Coverage** (Section 1.5).

---

## 23. Foundational Product Slice

The foundational slice — **Data Operations + Reconciliation Foundation** — is the proven product
baseline, not a recommendation. W1-W5 prove it through trusted data, paper validation, research
continuity, ledger reconciliation, accounting records, approvals, multi-asset coverage, and
governed reporting.

**Includes:** tenant profile; entity, account, capital account, ledger account, and journal entry
models; provider setup; file/API import with raw preservation; mapping profiles; validation rules;
normalized transactions, positions, and balances; reconciliation runs; exception queues; workflow
approvals; capital activity evidence; treasury-ledger posting controls; audit events; and a basic
reporting package.

**Existing evidence:** provider validation and data-confidence gates; paper-session readiness and
promotion review; portfolio ledger reconciliation and close-lane casework; governed report-pack
approval, provenance, and export evidence; accounting record summaries linking source data through
report-pack lineage; multi-asset operational coverage; shared Financial Record Explorers; browser
and WPF read models over shared contracts (desktop parity tracked as `W8-WPF-PARITY-001`);
completed statement connectors with completed Evidence Vault and statement-onboarding routes; and the
shared manual journal entry workbench over retained private-capital fund events.

**Remaining expansion work** (explicitly not implied complete): full treasury payment execution;
full alternative asset operations; forecasting and enterprise risk engines; proof-layer expansion
beyond the accepted W5X control-center boundary; complex capital structure modeling; full client
portal; no-code workflow designer; full live-trading readiness; Backtesting Studio beyond
evidence linking; mobile applications.

**Why this slice works** — it proves the core value chain:

```text
Fragmented data
→ Controlled import
→ Validation
→ Reconciliation
→ Review
→ Approval
→ Evidence
→ Reporting
```

---

## 24. Roadmap Alignment, Scope Gates, and Claim Rules

**Canonical truth order:** `docs/roadmap/data/*.yml` → `docs/roadmap/generated/*` → this design
charter → `docs/product/README.md`. Execution, evidence, and readiness status belongs in
`docs/product/implementation-todo-list.md`, not in this document.

**Active scope gate.** A capability belongs in active scope when current source evidence, roadmap
status, or user direction supports it. New work must strengthen data confidence, reconciliation,
approvals, accounting records, close support, retained evidence, workflow controls, or governed
reporting before expanding research, live trading, payments, forecasting, risk, portal,
workflow-designer, mobile, or scale-out lanes. Prior baselines are sequencing anchors and evidence,
not development ceilings — but under the activation doctrine (Section 2.1), expansion competes
against activation and activation usually wins.

**Claim rules.**

* *Complete* — roadmap row done plus generated proof plus operational evidence reference.
* *Supported foundation / experimental* — named in this charter with supporting source or design
  artifacts, no acceptance gate yet.
* *Planned* — registry state explicit; never presented as shipped.
* Roadmap acceptance is bounded to the named capability; release claims additionally require the
  P0 tracker, packaging, operator preflight, and required GitHub Actions evidence on one commit.

**Deferred lanes** (each reopens only through an explicit registry decision with its minimum
evidence defined in `docs/product/deferred-expansion-boundaries.md`): native live payment
execution; full alternative-asset operations beyond the accepted multi-asset baseline; enterprise
risk; forecasting and scenario engines; complex capital-structure modeling; broad administration
dashboards; broader self-service reporting and analytics beyond the governed platform baseline
(`W9-REPORT-005` client-grade output is delivered and accepted; what remains deferred is self-service reporting and analytics beyond it); broad client portal; no-code workflow
designer; document-portal collaboration beyond the Evidence Vault boundary; broad collaboration
tooling; mobile (closed).

**Prohibited as scope creep** from external inspiration: full cap-table system, outsourced services
operation, live payment processor, broad investor portal, autonomous-agent workflows bypassing
operator evidence.

---

## 25. Design Backlog and Version History

### 25.1 Rewrite Discipline

Version 1.0 is a consolidation, not an expansion: it adds **no** new product scope. It sharpens the
value proposition (Sections 1–2), incorporates registry facts through snapshot 2026-08-03 (the W9
slate, `W8-UX-CONSOL-001`, `W9-ASSET-010`), and encodes the activation-over-expansion, proven-slice,
and truth-discipline doctrines motivated by the 2026-07 adversarial program review. Scope changes
enter through the roadmap registry, never through this document alone. Detailed execution tracking
stays in [`docs/product/implementation-todo-list.md`](implementation-todo-list.md).

The superseded Version 0.25 text — including the Executive Marketecture Deck and the v0.15–v0.20
addenda in their original form — is preserved at
[`archive/docs/design/meridian-design-document-v0.25.md`](../../archive/docs/design/meridian-design-document-v0.25.md).

### 25.2 Version History

| Version | Change |
| --- | --- |
| Draft v1.0 (imported) | Original design draft imported from an external attachment |
| 0.15 | Accounting records and operational evidence release package; W1-W5 anchored as the operational record baseline |
| 0.16 | Private-capital operations and treasury-ledger research addendum (Carta, FundStudio, Modern Treasury patterns) |
| 0.17 | Shared Financial Record Explorer productization target |
| 0.18 | Operational proof layer market-gap update; Operational Evidence Graph surface |
| 0.19 | No-code provider integration manifest design |
| 0.20 | Customer-neutral operational-finance architecture clarification |
| 0.21 | LedgerGraph OS / Close, Data and Evidence Control Tower positioning; Number Passports; Verified Coverage |
| 0.22 | Reconciled with roadmap and tracker evidence (W1-W5, FREX, FINOPS complete; W6/W7 gated) |
| 0.23 | Bounded `W7-LIVE-001` governance gate promoted to complete |
| 0.24 | WPF product/UI work marked deferred |
| 0.25 | WPF desktop workstation reactivated as a co-equal lane focused on web-UI parity (`W8-WPF-PARITY-001`), superseding all earlier WPF-deferral statements |
| **1.0** | **Ground-up rewrite: proven-numbers value proposition; activation-over-expansion, proven-slice, and truth-discipline doctrines; W9 slate and current registry posture incorporated; structure consolidated from 27 sections to 25 with all normative invariants preserved** |
