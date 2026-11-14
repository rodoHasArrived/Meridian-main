---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-11-13
---

# src/Meridian.Ui/dashboard

The Providers route also includes on-demand backfill tier/priority controls, a read-only routing
preview, validation profiles and bounded quota history. Tier defaults and ceilings come from the
server catalog; choosing a paid tier declares the operator's subscription and does not verify it.
Numeric priority and Move up provide keyboard alternatives to dragging. Preview accepts symbol,
daily date range and proposed priority/availability without saving or fetching prices; unavailable
vendors, fabricated selections and unverified coverage remain explicit. Validation relaxation
requires a reason and exposes retained change history. Quota history distinguishes an observed
throttle from the next provider attempted and discloses process-lifetime retention.

Settings and Providers share the setting-activation banner. Save receipts distinguish `Live`,
`ReconnectProvider` and `RestartHost`; pending field names remain inspectable until host activation.
No control in these panels edits credentials, deployment tenancy, storage paths, provenance stamps,
IB pacing or live execution. Focused tests use the corresponding `data-screen.*` panel suites and
`settings-change-impact.test.tsx`; browser acceptance uses synthetic responses, never live credentials.

Canonical symbol registry includes an on-demand mapping editor over the shared CRUD and CSV
import routes. Operators can correct provider aliases, inspect retained Compare mismatches and
stage Compare → Canonical independently for IB, Alpaca, Polygon and Yahoo. Promotion requires
review, a recorded reason and explicit acknowledgements that existing archives are not rewritten
and raw/adjusted series remain separate. Active and saved mode are displayed separately; mode
changes require host restart. Authoritative failures disable writes while preserving the draft.
Focused coverage: `data-screen.symbol-mapping-editor.test.tsx` and existing canonical registry tests.

The Data provider workspace includes an on-demand streaming failover editor. It reads authoritative
saved settings, distinguishes unavailable runtime state, and refreshes the active provider after a
committed switch/reset. Settings and rule create/edit/delete use shared guarded API saves and show
pending host restart; relaxed rule thresholds require an audited reason. Focused coverage lives in
`data-screen.failover.test.tsx` and the shared `FailoverEndpointTests`.
Deleting a rule also requires an explicit operator reason and retains the removed protection snapshot.

## Fixed-income workflow access

Asset detail and selected bond holdings link to the existing Security Master analytics and
convention-migration workflows. Symbol-only holdings resolve an exact Security Master identity;
ambiguous matches require selection. `securityId`, `tab=evidence` and `bondAction=analytics|migration`
retain the requested action on reload. Explicitly linked instruments control the explorer selection;
an absent row never substitutes the default instrument. Initial action focus is applied once, and
manual record selection updates the link. The explorer's initial automatic selection replaces the
current history entry, so Back leaves Security Master in one step. Subsequent explicit selections
create history entries; Back and Forward restore their exact instruments. Instrument reads disable
development fallback.

Issuer and maturity ladders are browsable before selecting a security. Keyboard-accessible row
actions open authoritative instrument detail. Command search includes analytics, convention
migration, ladders, account forecasts and reviewed close prices. Account oversight is visible in
Reconciliation with account-owned book selection and explicit access/configuration blockers.

Cash-ladder contributions link instruments and Security Master evidence. Their inline inspector
reads the exact Asset Operations projection identity, engine and retained terms version/hash;
unavailable original evidence remains a blocker. Asset Operations and contractual scenario run
identities are distinct. Contractual scenarios expose multiple prepayment intervals, contract-derived
stream selection and separate explicit hypothetical notice dates. Account forecasts already support
dated FX: preparation can reuse a selected retained snapshot's observations, flows and dated FX with
unchanged amounts/evidence, then capture a reviewed linked correction. First-capture preparation
in this account-oversight workflow still requires reviewed source inputs; the separate account/bond
planning workflows below do not assemble that forecast command automatically.

`lib/workstation-features.ts` exports `WORKSTATION_CAPABILITY_TRACES`, mapping the ten fixed-income
capabilities through service, API, client, screen/action, permission and browser acceptance evidence.
Missing seams and input-preparation limits stay explicit; catalog metadata never grants access.
The current journeys below include curve/version management and selection, pricing-source controls,
settlement previews, canonical amortization draft handoff and provider discovery. Remaining work
includes browsable contractual run history, account-oversight first-capture preparation, contextual
factor-paydown launch, account/evidence selectors and supported amortization reversal/FX coverage.
These limits and pending complete-workflow browser acceptance remain in the
[access plan](../../../docs/engineering/plans/bond-capability-expansion.md#existing-capability-access-next-delivery-priority).

Focused validation includes the changed screen/component tests, `fixed-income-capabilities.test.ts`,
and `node scripts/dev/verify-fixed-income-access.mjs` from the repository root against a running Vite
server. Browser evidence uses deterministic synthetic API responses, not live provider certification.

## Fixed-income and accounting journeys

Security Master exposes **Curves: definitions, pillars and historical versions** before security
search, including an empty master. Operators create immutable definitions, publish multiple dated
pillars, and inspect or compare retained versions with their source, timestamps and content hashes.
Bond Evidence includes yield analysis with an explicit terms version and separate curve valuation
with exact retained Discount and optional Forward:index versions. Curve values and settlement
economics arrive as decimal strings; the browser does not recalculate them.

Portfolio holding inspection links to Asset Detail. Its **Trading Parameters → Prepare bond
settlement** action exposes the existing preparation service for bond acquisitions. Explicit terms,
dates, face, booked factor, clean price, fees, observed cash, retained evidence and distinct accounts
produce a reconciliation review or named blockers. Preparation creates no trade, journal or lot.

Accounting's journal workbench exposes **Accrual schedules** with reviewed fixed amounts, cadence,
dates, accounts, source evidence, authoritative period preview, occurrence generation and the
retained journal queue. Account-owned and sleeve-owned books accept a reviewed fund-account
identity, checked against retained book membership before configuration. Ledger Explorer exposes
**Canonical lot amortization** using acquisition
policy and retained lot economics, schedule previews and an explicitly reviewed occurrence. Draft
handoff recomputes the preparation fingerprint and enters the existing immutable journal workflow;
the reviewed occurrence is retained on its authoritative position, preserving all journal dimensions.
Independent approval and posting authority remain there. Missing scope, permissions, required
services, closed periods, changed inputs and uncertain writes remain explicit blockers.
The canonical amortization handoff accepts matching acquisition/functional currencies at unit
acquisition FX; unsupported FX lots stop before calculation or retained source/draft writes.

The account oversight **Close prices → Maintain pricing sources** action maintains that exact
account/security's ordered source hierarchy, staleness limits and effective UTC version. Unknown
save outcomes retry the same reviewed command. Existing retained price sets keep their evidence.

Asset Detail and Loan Book cash-flow scenarios support multiple nonoverlapping per-stream
`[start, end)` prepayment assumptions, PSA seasoning and recast policies, plus immutable benchmark
curve pins. Preview, retention, retrieval and replay preserve the exact decimal inputs and manifest.

Security Master's **Discover provider bonds and stage candidate imports** action searches the
configured Alpaca fixed-income provider, exposes missing services or provider failures, and requires
candidate review before staging an identity-only asset-information upload. It does not infer coupon
or maturity terms or create approved security economics. Staging uses the existing retained upload
review path and requires Modify Security Master. Provider evidence is synthetic-verified;
live-provider compatibility remains live-untested.

Workflow tests live beside each feature: `security-curve-workbench`, `security-curve-valuation-panel`,
`bond-settlement-panel`, `accrual-schedule-workbench`, `accounting-screen.canonical-amortization-panel`,
`pricing-source-hierarchy-panel`, `contractual-cash-flow-workbench`, and `bond-discovery-panel`.

Shared `ExactCurveInputs` rows pair a calculation role with a definition and an explicitly selected
immutable version. Changing the curve clears its version; loading, empty and retry states stay
visible. Use these rows only in calculations that consume curves, and validate role semantics in
the owning workflow. Never silently select the latest version. Structured retained-evidence fields
keep identity, source, custody, dates and digest/version separate and labeled. Recurring source
references and reviewed canonical accounting identities have different contracts and must not be
interchanged. Preview edits invalidate prior results; consequential handoffs require review, retain
one command through uncertain outcomes, and expose the exact retained draft rather than a generic
journal destination.

Strategy grid cells, maturity bars and ownership-map nodes include the same displayed text and
word boundaries in their accessible names, retaining coordinates, currency and full record
identity. Margin SVG labels inherit the current foreground in light and dark themes.

Execution notifications retain an accepted stream snapshot when an older snapshot request finishes.
Execution-alert preference loading and save feedback stay bound to the current account and attempt;
account changes and unmounting retire older callbacks. The preference, journal and server-preview
controls use the shared keyboard-focus and target-size contracts. The journal's **Before date**
filter uses an exclusive UTC end boundary, matching the execution summary.

Financial visualization panels extend the existing Loan Book, Bond Analytics, Margin Control,
Trading order inspector, Market Data desk, Family Office, corporate-action inbox and disposal tax
details. React owns SVG/HTML rendering and table/inspector selection; monetary aggregation and
financial decisions remain shared-service responsibilities. The Options tab supports contract
screening and arbitrary listed-option/equity baskets with dated imported broker assessments.
Private-capital observations use the separate `private-capital-records.api.ts` consumer; the
issuer-side `private-capital.api.ts` workflow retains its existing call/funding authority.
Authoritative imports have explicit permissions, source evidence, preview/acceptance, correction
history and replay identities. See `docs/operators/financial-visualizations.md` from the repo root
for meaning, entry paths and recovery. Focused tests live beside the panels and API consumers.

Bond details retain the existing analytics panel and add original-face purchase previews,
all-model cash flows, retained quote selection, separate provider and Meridian yields,
partial metric coverage, rate/spread shocks and account-owned saved comparisons. All amounts
come from the shared workflow API; decimal input text is preserved, and edits abort stale
calculations. Purchase/comparison economics use an explicit UTC date and time, captured in
the input for repeatable retries; retained quote selection leaves economic and knowledge
cutoffs unchanged. Settlement and reference screening retain their date-only meaning.
Combined bond screening uses bounded server pages and agency-specific ratings;
reference knowledge-time limitations remain visible. Retained market observations can be
inspected or imported with source evidence, and capability checks separate configuration,
entitlement and verification evidence.
Portfolio Cash Ladder adds account/book/date income planning, separate native-currency ladders,
collection/watch history, reviewed-staging holdings acceptance and portfolio risk coverage.
Missing opening cash and FX remain explicit. US GAAP income previews read the server-owned
lot and book election; previews, imports and saved comparisons create no journals or payments.
Receipt corrections retain the original plan and prior snapshot with new immutable identities.
Historical performance keeps observed mark/cash changes, model attribution and accounting
income separate, with missing transaction coverage and unexplained residuals visible.
Coupon recognition keeps retry-stable requests and reviewed fingerprints; preparing a canonical
Income source continues through governed review without posting a journal or initiating payment.
Contractual scheduled-principal treatment is offered only after an authoritative GAAP preview
confirms adopted bond-income facts on every lot, and requires a new preparation after selection.
Retained risk scenarios can be reopened with their original calculation context. Performance
transaction imports accept reviewed source identifiers only; coverage is resolved by the server.
Coupon reversals name the posted receipt and correction rationale and follow the same preview
and governed preparation flow, preserving the original journal and receipt.
The retained market-input panel exposes bond-specific OpenFIGI discovery and typed FRED intake
through the shared APIs. Discovery keeps ambiguous candidates unresolved and does not amend
identity; FRED intake derives series units from an independently accepted provider binding and
keeps missing observations and live verification status explicit.
Focused coverage is in the `bond-workflow`, `account-bond-planning` and `bond-income` tests.

The Strategy Run Ledger Explorer supports an inclusive UTC as-of date, retained in its URL and applied to both trial balance and journal. Clearing the date restores the complete run. Historical mode omits the final-run generic proof explorer so final balances and links cannot appear under a historical scope. Accounting package downloads preserve run, date and selected CSV/Excel/Parquet/Arrow format; packages declare missing lot snapshot evidence explicitly.

Accounting's entity and intercompany view uses shared host contracts for authorized entity history,
accounting profiles, source-linked proposals, breaks and versioned consolidation. Each accounting
profile pins a canonical approved legal agreement ID, integer version and payload hash; operators
configure book/account mappings without copying or editing legal terms. Legal authoring and
independent approval remain in Accounting Configure's canonical agreement registry. Financial amounts and
capabilities come from the host. Current draft scope must match the preview before mutation;
loading, stale, blocked and unavailable states remain visible. See
[intercompany operations](../../../docs/operators/intercompany-entity-controls.md).
Guarantee mappings and FX options use shared keyboard-focus and target-size controls; disabled
commands explain their existing blockers. Controlled-close receipt navigation retains the server
response's journal, book, profile and version even when the next request is edited; incomplete
receipt identities provide no journal link. Active FX edits retain their as-of scope until save or
explicit cancel, and pending saves retain the mounted editor.

Settings provider runtime evidence and integration setup components are owned by
`settings-provider-integration.tsx`; the Settings screen composes them without changing routes
or mutation authority. Presentational Settings sections live beside the screen:
`settings-screen.profile-sections.tsx` (profile authentication, system posture, appearance),
`settings-screen.brokerage-sections.tsx` (Alpaca and Robinhood), `settings-screen.diagnostics-section.tsx`,
and `settings-screen.capability-sections.tsx` (runtime and backend capabilities), with shared chips and
tone helpers in `settings-screen.section-primitives.tsx`. The state-heavy sections pair a private
`useSettings<Section>SectionState` hook with their component in `settings-screen.scoped-access-section.tsx`,
`settings-screen.operations-section.tsx`, `settings-screen.asset-profile-section.tsx`, and
`settings-screen.provider-section.tsx`; draft-state shapes shared with the screen live in
`settings-screen.section-state.ts`. The screen keeps shared state, the effects that reconcile it with
incoming props, the mutation-confirmation dialog, and the route-driven section gates. Accounting Security Master selection/search state belongs to
`accounting-screen.security-master.ts`, while schedules, corporate actions, open lots and
instrument-passport projections belong to `accounting-screen.security-projections.ts`.
The Accounting screen composes its larger panels from sibling modules:
`accounting-screen.external-gl-panel.tsx`, `accounting-screen.approvals-workstream.tsx`,
`accounting-screen.case-workbench.tsx`, `accounting-screen.manual-journal-panel.tsx`, and
`accounting-screen.configuration-panel.tsx`, with `accounting-screen.accounting-value.tsx` holding the
shared label/value cell.
`src/lib/api.ts` is the stable import facade for the workstation API client: it re-exports the
transport in `src/lib/api/http-client.ts` and the domain endpoint modules in `src/lib/api/*.api.ts`
without widening the public surface. Import from `@/lib/api`; endpoint modules import the transport
from `@/lib/api/http-client`, never from the facade.
The existing view-model entrypoint re-exports their public contracts for current callers.
The design-system source contract checks the projection owner, those re-exports and the screen's
schedules-view binding together.
The existing four fixed-number and one signed-money formatting ratchet allowances follow their
unchanged declarations into the projections module; aggregate ceilings remain unchanged.
The Settings typography allowance likewise follows the unchanged JSX: 16 occurrences remain in
the screen and 45 belong to the provider components, retaining the prior aggregate ceiling of 61.
Fund-structure setup transport types live in `lib/api/fund-structure-setup.contracts.ts`, with
`lib/api.ts` retaining its public type exports. Promotion form normalization, keyed evidence validation
and checklist presentation live in `trading-screen.promotion-evidence.ts`; the existing view-model
entrypoint retains its public exports, React gate state and command transport. Payloads, evidence-token
rules and approval semantics are unchanged. Three fixed-number formatting allowances follow their
unchanged checklist declarations; the original aggregate allowance of six remains unchanged.

Strategy overview composition lives in `strategy-overview-hub.tsx`; equity-path comparison state,
load results and presentation live in `strategy-screen.equity-paths.view-model.ts` and
`strategy-screen.equity-paths.tsx`. The existing Strategy
view-model entrypoint retains its public exports while both screen entrypoints stay within their
file-size limits. Curve sampling, reported returns and promotion selection behavior are unchanged.

Trading promotion form normalization, keyed evidence parsing and request validation live in
`trading-screen.promotion-evidence.ts`; the existing trading view-model entrypoint re-exports its
public contracts. Gate criteria, retained evidence requirements and command transport are unchanged.
Reference Data probe payload classification lives in `lib/reference-data-probe.ts`; probe
orchestration lives in `lib/api/system-overview.api.ts`, and the `lib/api.ts` facade keeps the
established public type exports.

Fund Structure setup contracts live in `lib/api/fund-structure-setup.contracts.ts` and the setup
request functions in `lib/api/fund-structure-setup.api.ts`; `lib/api.ts` re-exports both. Pure order-ticket form,
validation and control-state helpers live in `trading-screen.order-ticket-state.ts`.
`trading-screen.view-model.ts` retains service and hook orchestration and re-exports the public
helpers and types. The extracted declarations and function bodies preserve the existing request
shapes, validation, acknowledgement and status behavior.

The Evidence Workbench's retained-document panel reads shared single-principal declaration,
source-snapshot and receipt endpoints without development fallback. Evidence acknowledgment is
default-off and uses explicit grants, a current declaration, a reason and an exact confirmation.
It leaves document Accept/Reject unchanged. Retained receipts show the preparer disclosure,
source provenance and unassessed current support separately; unknown schemas or decision types
display classification unavailable. Withdrawal preserves history, self-revocation appends a
correction, and uncertain writes retain one command for retry. Focused coverage lives in
`single-principal-attestation-panel.test.tsx`, `single-principal-attestation-api.test.ts` and
`evidence-workbench-screen.view-model.test.tsx`.

Evidence packets and scoped amount Number Passports show the server's separate preparer-evidence
projection with task counts, exact-source/stale/revoked applicability, synthetic or unclassified
provenance, and an explicit boundary against financial approval and Verified Coverage credit.
Unknown projection or receipt versions remain unavailable. Original receipt downloads preserve
the authenticated artifact bytes and check receipt identity without rewriting financial JSON;
permission failures never fall back to fixture data. Reconfirming an offered policy requires an
explicit reason and records a new declaration revision. Focused projection/export coverage lives
in `preparer-attestation-evidence.test.tsx` and the existing attestation API/panel tests.

Native corporate-action cases with a bound accounting book expose a separate cash-dividend
attestation panel. It reads the server's one-USD-book policy and capabilities, keeps declarations,
preparer attestation and posting as explicit reasoned commands, and never posts after an attestation
automatically. Read-only monetary operands and retained eligibility hashes stay server owned.
Enabling the book declaration separately confirms the preview's exact existing receivable/income
accounts and promoted rule binding, requires the server's accounting-role binding capability, and
retains those original operands for command recovery. Withdrawal preserves the retained binding.
Uncertain commands retain one identity in session recovery bound to actor, tenant, company, book
and case; restoration strips any locally injected authority/context fields. Amount passports show
compact native accounting-decision disclosures separately from evidence-task receipts and ordinary
approval, including exact contributing journal identity. Full book entitlement JSON is not exposed
through the amount renderer. Coverage: `cash-dividend-attestation-panel.test.tsx`,
`cash-dividend-attestation-api.test.ts` and `accounting-preparer-attestation.test.tsx`.

Accounting Configure includes a tenant-scoped Intercompany Agreements registry using shared
FundStructure contracts. Operators can save proposals, submit for independent review, inspect exact
approved versions/evidence and retain local input across conflicts. Journal source drill-through
keeps transaction and functional amounts separate and labels agreement coverage as advisory.
Selected-line agreement authoring requires the server's explicit compatible-writer capability;
historical agreement/version references remain visible when that capability is unavailable.
The new-draft control explains registry loading, an open draft, unavailable authority and access
restrictions through the shared button's accessible disabled reason.
Focused coverage: `accounting-screen.intercompany-agreements.test.tsx`, `intercompany-agreement-editor.test.tsx`,
`intercompany-journal-reference.test.tsx`, `intercompany-agreement-journals.test.tsx` and
`intercompany-agreements.api.test.ts`.

Run the focused checks from the repository root:

```bash
cd src/Meridian.Ui/dashboard
npx vitest run src/screens/accounting-screen.intercompany-agreements.test.tsx src/components/accounting/intercompany-agreement-editor.test.tsx src/components/accounting/intercompany-journal-reference.test.tsx src/components/accounting/intercompany-agreement-journals.test.tsx src/lib/api/intercompany-agreements.api.test.ts
```

Floating analytics distinguishes `Select observed fixing` for already-reset coupons from `Select
forward curve` for future resets. The observed-fixing panel verifies and retains exact-date source
evidence, then explicitly selects its immutable receipt for the mounted calculation. Returning keeps
the model, dates, quote receipt or manual amount and cutoff. Existing receipts remain selectable if
the provider is unavailable; a cutoff preceding retention still blocks until deliberately reviewed.

Bond ladder selection loads authoritative Security Master detail even outside current search results
and opens the existing Evidence tab. Return to bond ladder preserves its query, loaded rows and
scroll position. Analytics offers Use retained observation with source, time, freshness, quote status,
clean/dirty quotation and settlement evidence. Calculation submits the immutable receipt identity,
not a copied quote. Blocker actions focus convention review, source selection or rates/fixings while
retaining calculation inputs. Decimal text stays exact through parsing and formatting.
Reference-data probes wait for authoritative selected detail and retain its currency and identifiers
even when the bond is absent from the current search. Loading, failed or mismatched detail issues
no probes with invented selection context. Injected callers without a detail loader can use an exact
search row. Coverage is in `accounting-screen.view-model.test.ts`.

Command search finds these existing fixed-income workflows by task: `bond analytics`, `bond ladder`,
`floating coupons`, `observed fixings` and `rates ingestion` open Security Master. Ladders can be
browsed before selecting a security; selected-bond analytics uses its Evidence tab and reference
probes retain its authoritative identity. `cash-flow scenarios` opens Asset detail;
`coupon income` and `bond maturities` open the portfolio cash ladder. `account oversight`,
`account cash forecast` and `close prices` open Reconciliation casework. `MBS paydown` opens Factor
paydown, and `bond calculation cutover` opens Accounting Configure. These aliases use existing
routes and preserve their permissions and context; they do not assert provider availability or
add support for an unsupported calculation model. Regression coverage is in
`command-palette.view-model.test.ts`.

Integration dry-runs show provider market value, normalized bond consideration and variance together,
with an explicit sample-payment preview for fixed-income conventions. Retained source and derived
normalization remain inspectable side by side.

The main order ticket retains local quantity and limit/stop-price text while the existing numeric
model drives preview, validation and submission. Typing `188.0` followed by `5` preserves `188.05`.
Instrument, order-type, account-scope and close/reset transitions discard stale editor text;
account changes invalidate review acknowledgement in their first render. Numeric transport and
server authority remain unchanged. Native number inputs can normalize characters before validation;
the strict decimal parser validates the text the browser actually supplies. Focused coverage lives
in `trading-screen.test.tsx` and `trading-screen.view-model.test.ts`.
Pure ticket control and invalid-field projections live beside their wording in
`trading-screen.order-ticket-text.ts`. Definitive order-refusal classification lives in
`api-errors.ts`; the existing `api.ts` facade retains its named export.

The supported USD-equity order flow also requires a current shared-server preview and operator
acknowledgement before submission; edits invalidate that preview, and submission rechecks admission.
An unconfirmed outcome holds the submitted account/client identity until Refresh checks finds a
terminal retained journal outcome. Orders exposes that journal, notifications link to its evidence,
Settings Preferences holds account-scoped execution alerts, and Strategy shows retained run
comparison. These surfaces consume shared services: unknown costs and incomplete comparison remain
explicit, and agreement grants no promotion authority. See
[execution workflows](../../../docs/engineering/execution-workflows.md). Focused browser coverage
lives in `trading-screen.server-preview.test.tsx`, `trading-screen.journal.test.tsx`, and
`lib/notification-center/execution.test.ts`.

Native capital-call previews accept the server's canonical case-normalized call identity while
retaining the reviewed plan hash; a preview for another call is rejected before retention.

Security Master's Schedules tab renders the shared declared call/put outcome, including stable
blocker codes and reasons, evaluation date, retained security version, resolved window count and
notice diagnostics. An absent outcome keeps the undeclared distinction. The same panel shows the
host's effective governance profile, its source and restart latch, edit/review requirements and
ignored configuration. Missing governance evidence is explicit. Put and call/put notice-deadline
rows use operator-facing labels. Focused coverage: `security-master-policy-evidence.test.tsx`.
An independent cash-flow calculation blocker (including `NegativeInterestUnsupported`) stays visible
even when the record's declared exercise dates resolved successfully.

Data overview leads with provider health and scoped retained verification events before backfill
work. A provider card's "Latency" is a reported duration or "Latency not reported", never a
response time; the provider table's "Trust / last good" column (`latencyOrLastGoodText`) falls back
to the last successful or verified time. Verification probes are discrete observations, not uptime;
absent, pending or malformed history is explicit. Backfill coverage cells retain execution state and requested dates without
asserting that every market session is complete. Settings overview is a setup checklist based on
loaded connection and session evidence, with default browser preferences counted as valid.
Shared chart alternatives retain exact source values through the table workbench. Complete server
exports read their authorized source before filtering. Upload and workbook downloads explicitly
export all filtered preview records, with preview-only metadata; they do not claim whole-file coverage.
Account history keeps cash and securities
components separate by currency and source; it does not substitute strategy-run equity or derive
an unprovided account net-equity total.
Close burndown renders only retained canonical workflow snapshots and breaks the line across
missing snapshots or workflow-version gaps. Legacy histories and one-point observations remain
explicit. Metric payloads may carry a version-1 Number Passport reference to the existing
subject-addressed evidence API; missing references remain unverified, and this additive contract
does not grant W10/W11 roadmap acceptance.

Bond records in Security Master expose calculated analytics and a convention-migration preview
under the existing Evidence detail tab. Operators supply dates and a clean price or annual yield;
the browser displays server-calculated per-100 values, annual-fraction yields, projected payments,
input hashes, operation support, and blockers separately from imported Pricing & Risk fields.
Authoritative reads prohibit development fallback. Changing inputs or security selection clears
prior results and aborts pending requests. Migration preview requires the loaded terms version and
an explicitly selected coupon unit; it compares prior and candidate payments without publishing
terms or changing journals. Staging requires an explicit review justification and submits the
request and opaque fingerprints; displayed financial projections are never round-tripped into a
command. The server recomputes the preview before creating a governed draft. Stale or uncertain
outcomes require inspection and refresh; approval and publishing remain in the existing workflow.
Focused coverage: `bond-analytics.api.test.ts` and `bond-analytics-panel.test.tsx`.

Settings provider runtime evidence includes a side-by-side identifier matching table. Conflicting
CUSIP, ISIN, provider, ticker and internal identifiers require an explicit active security choice
and resolution reason. The server retains the authenticated operator and exact matching hash;
changed evidence requires review again. Source records and all conflicting matches stay visible.
Focused coverage: `provider-identity-evidence.test.tsx`.

The Calculation version selector defaults to v2 and retains explicit v1 replay. Choose v3 for a
typed Floating bond with a named index; the shared service verifies eligibility. V3 shows projected
coupon payments with observed/forecast reset evidence, index/coupon annual fractions, spread in
basis points, retained curve identities, projected-flow yield, and separate DV01/SpreadDv01 units.
Yield is not discount margin. Model changes clear pending/results state; incompatible or missing
inputs remain typed refusals with no metrics, payments or model fallback. See the
[synthetic SOFR walkthrough](../../../docs/engineering/plans/bond-capability-expansion.md#workstation-floating-analytics).

Provider credential editors require explicit selection of an existing owned connection. Status,
save, verification and deletion use the same retained connection; account and environment come
from authenticated ownership. Missing or changed ownership blocks changes without provider-wide
fallback. Refresh or session changes clear secret drafts and invalidate delayed responses. This
client increment does not create ownership, migrate legacy secrets or bind host runtime providers.
Family matching uses only the server's explicit provider aliases and preserves each retained
connection's raw identity, tenant, account and environment.
Live/production acknowledgement also recognizes case and surrounding whitespace in retained
environment labels without changing the environment sent to the server.

Accounting → Factor Paydown (`/accounting/factor-paydown`) provides the governed paydown workbench.
Ledger Explorer links to it and to `/accounting/consolidation`, preserving book and period scope. Exact factor text and
existing retained evidence identities go to the shared preparation endpoint; the server supplies
principal, basis relief, realized results and per-lot allocation as exact decimal strings. Creating
a review draft requires an explicit description and confirmation of the preview. The shared draft
adapter rechecks its fingerprint and retains the accounting-event candidate plus its exact journal
workbench draft. The scoped link selects that exact draft for validation and review submission.
Asset-linked drafts remain immutable. The journal lifecycle panel loads authenticated protocol-1
capabilities for the exact draft, book, period, version and authenticated actor; old, absent or
mismatched capabilities keep approval and posting unavailable. Supported decisions require explicit
review and a rationale.
The server retains the real independent approver, candidate fingerprint and receipt bytes; the
browser checks their SHA-256 digest before offering the receipt download. Uncertain outcomes retain
one exact command and UUID in session recovery; session changes and actor mismatches clear prior
operator retry state. Pending posting resumes the server's original command unchanged, even when
the recovery version advances, and blocks all other mutations.
Canonical posted journal links come only from the returned durable posting receipt. Editing, Save,
reversal, rebook and close-lock remain unavailable. The browser submits reviewed decisions to the
shared governed workflow and never calculates financial amounts or roundtrips numeric lot instructions. Book,
period and tab changes discard preparation, stale responses are ignored, and uncertain writes keep
the same reviewed request for retry. Focused tests are in
`accounting-screen.factor-paydown-panel.test.tsx`, `factor-paydown.api.test.ts`,
`accounting-screen.asset-workflow.test.ts` and `asset-accounting-workflow.api.test.ts`.

## Financial visual evidence

Report-writer chart previews are explicitly labeled **Signed value summary** beside the configured
chart type. They use a symmetric scale around a visible zero baseline; missing, non-numeric and
non-finite observations remain unavailable. Reported values, series labels, warnings and the
underlying grid stay inspectable. The summary does not claim to render the configured chart type.

The retained income comparison adds a baseline → journal → population → methodology → unexplained
→ current waterfall. Exact decimal strings are summed and checked with integer arithmetic before
only bounded drawing coordinates become numbers. Missing, incompatible or inconsistent bridges
withhold the plot. The server residual is included once; unexplained records remain separate, and
nonzero unsupported records cannot produce a reconciled success banner through netting. Each
contribution opens the existing retained-support sheet, preserving URL state and keyboard return.

Open-position exposure and P&L cells retain their original financial strings with small relative
bars. A strict presentation adapter accepts only the source's demonstrated invariant dollar formats
(including grouped values and K/M suffixes). These are rounded display comparisons, not concentration
calculations or an inferred ISO currency. Each column uses its own largest absolute supported value
across all loaded positions before filters, grouped by source currency. Rows without a currency stay
in a separate, explicitly unavailable-currency group; no currency conversion is applied. Unsupported formats retain
the original amount and say Graphic unavailable. Losses extend left of a visible zero marker;
the largest displayed exposures with the shared mark-review assessment receive a textual cue.

Focused coverage: `report-writer-grid-format.test.ts`, `report-writer-chart-preview.test.tsx`,
`income-movement-waterfall.test.ts`, `reporting-income-comparison.test.tsx`,
`position-value-graphic.test.ts`, `position-value-graphic.test.tsx`, and the existing position-control
and Portfolio view-model tests.

## Portfolio Open positions controls

Open positions supports case-insensitive symbol search, exact Side filtering from loaded rows,
and shared-assessment Current/Review required filtering. Filters combine with AND. Symbol, Side,
and Mark readiness headers sort stably; no sort retains source order. Financial strings and totals
are not sorted or recomputed. Counts explicitly describe filtered versus loaded positions.

The toolbar remains available for both no-data and no-matches states. Filtering never changes
holding selection or row identity: a selected holding outside the results retains its evidence and
shows Clear filters, which preserves sorting. Reset controls also removes sorting. The existing
row cap and Show all control remain in place for large result sets.

Controls round-trip through the optional `positionsTable` member of the existing v1
`portfolio-workstation` view envelope. Search updates the URL after 250 ms with replacement;
blur flushes it before Copy link/Save view. Discrete filters and sorts create history entries.
Back/Forward or external navigation cancels pending writes and restores URL controls. Scope,
other query parameters, hash, and existing same-screen envelope fields are preserved. Old or
invalid controls default safely without rewriting the URL on load. Link-budget failures are
explicit, and controls stay applied locally. No new selection persistence is introduced.
Saved layouts accept internal table-state routes beyond 512 characters while retaining the existing
complete-envelope size limit. An invalid or oversized layout reports failure and keeps the save
dialog open; it cannot claim success with an empty route or an unrestorable persisted record.

Focused coverage: `portfolio-positions-table.test.ts`, `use-portfolio-positions-table.test.tsx`,
`portfolio-screen.positions-table.test.tsx`, and existing Portfolio/a11y suites. Brokerage positions
and Trading blotters retain their existing interaction models.

## Shared workstation header

The notification bell counts unread events, separately from unresolved financial work.
Its operator inbox belongs to the current account scope: changing accounts immediately
withholds old rows and cancels obsolete reads. Polls do not overlap, and reads time out
after 15 seconds so a stalled request cannot prevent recovery. Failed inbox reads
clear prior inbox evidence and show an unavailable message with Retry inbox; independent
system events and price alerts remain available. Loading and unavailable counts explicitly
cover loaded feeds only. Regression coverage is in `use-notification-center.test.tsx` and
`notification-center.test.tsx`.

The browser shell renders `WorkspaceHeader` as one row above every route:

- **Left:** the breadcrumb, ending in the page name as the page's only `h1` (`Trading > Orders`;
  a workspace root reads `Trading`, the front door reads `Today`). The masthead shows only
  `Meridian` and announces the workspace to assistive technology, so the location is stated once.
- **Centre:** the permanent operating-scope bar (`components/meridian/operating-scope-bar.tsx`),
  `Scope  Entity … › Fund … › Account … › Period …`, with Subject, Run and Provider inserted
  before Period when set. An unset Entity, Fund or Account reads `All authorized` and an unset
  Period reads `All periods` (never "None"); set dimensions the current screen does not apply are
  muted and described as retained. Entity and fund ids print as names when the organization graph
  has loaded (`valueLabels`), otherwise as ids. It shows on every route and opens the scope picker.
  The empty scope as a whole reads `All authorized scopes` (`OPERATING_SCOPE_EMPTY_SUMMARY`), the
  same words Today prints.
- **Right:** Refresh, `Views` (saved-layout and save-current-view controls) and an overflow menu
  holding Copy link and the workspace's About text.

The route body carries the task title as an `h2` (`Orders blotter`); routes do not render their
own `h1`, repeat the workspace name, or repeat the breadcrumb's page name (`Accounting > Approvals`
is headed `Approval queue`, not `Approvals`). Accounting and Reporting task modes keep `label` for
the page name and carry the body heading in `title`. The browser tab title and the route-change
announcement name the page first, as the breadcrumb does (`Orders - Trading - Meridian`,
"Orders, Trading loaded."), without the old `Workstation` suffix. `Workflow context` lives in the scope picker, collapsed
under the scope controls; following one of its links closes the picker. Live, simulated/seeded,
unknown-provenance and degraded warnings remain outside disclosures.

The scope picker stages all changes, including Clear scope, until Apply scope. Cancel and Escape
leave the active URL and stored scope unchanged. The run dimension remains inspectable (read-only);
applicability text distinguishes dimensions applied here from those retained for other screens.

Entity and fund (P4) are scope dimensions carried as the `entityId` and `fundId` URL parameters
(`app-shell.operating-scope.ts`), in browser storage, recent scopes, saved layouts, Copy link and
the operator's server-side default scope. They are written into every route's URL because they head
the scope hierarchy, but they are applied only where a screen reads them: Accounting reads
`entityId` (close scope, intercompany and payment-approval panels); no screen filters by `fundId`
yet, so the bar shows a set fund as retained everywhere. Payloads are never filtered by entity or
fund on the client. The picker's entity, fund and account options come from the existing
organization read model `GET /api/fund-structure/graph?activeOnly=true`
(`lib/operating-scope/structure-options.ts`, loaded by `use-scope-structure.ts` when the picker
first opens or the scope names an entity or fund). It lists entities, funds with the entities they
hold, and accounts with their fund and entity; household brokerage accounts the graph does not list
are added without a relationship. Choosing an entity narrows the funds to those holding it and the
accounts to those booked to it; choosing a fund narrows the accounts to its own; an account with no
recorded fund or entity is left out of a narrowed list and counted, and a fund or account known to
sit outside a new entity or fund is cleared with a status note. The graph is gated to fund-structure
permissions; without it the entity and fund fields take typed ids, nothing is narrowed, and the
picker says so.

The picker's Period control sets the window: `All periods`, `Today`, `MTD`, `QTD`, `YTD`,
`Last month` (computed on the operator's calendar day through `LocalDate`,
`lib/operating-scope/period.ts`), `Custom range` on the shared `DateRangePicker`, or `As of` a single
date. Every choice replaces the whole window (`from`, `to`, `date`, `asOf`); a date set by an
originating workflow is shown as such until replaced. The bar prints the window as
`Period 2026-11-01 to 2026-11-14` (a same-day range prints the day once).
Navigating to another workflow disposes nested view-control dialogs and their keyboard listeners
before the disclosure closes; screen-local table URL updates retain the current control session.
Symbol suggestions support arrow keys, Enter, Escape, search progress, no matches and Retry.
Escape dismisses suggestions before closing the dialog. Navigation reveals the active destination
after route changes while respecting manual collapse until the next navigation. Applying a scope
replaces the route's scope query parameters (`appendOperatingScopeToRoute`), so the URL, Copy link
and every scope-carrying link hold it.

Every figures strip (`StatStrip`) prints the scope it was counted over without per-screen wiring
(`lib/operating-scope/figure-scope.ts`): a screen's own `scope` prop wins; otherwise the fund
account the supplying payload was loaded for (`Account …`, or `All authorized accounts`), then
`All authorized scopes` for a payload the shell loads without a scope filter (the selection does
not narrow those figures, so they do not borrow it), then the scope the shell applies on the route
(`OperatingScopeLabelProvider`). Figures that state their own scope keep it. Screens that need one
fund account use `InlineFundAccountPicker`, which sets only the fund-account dimension through the
shell (`FundAccountScopeSelectionProvider`); Trading uses it in place of its "Choose a fund
account" empty state in a non-live environment, on the overview and in the one-line row on Orders
and the other Trading routes. Live, unknown and failed brokerage states keep blocking.

Focused regression coverage includes `app.test.tsx`, `workspace-header.test.tsx`,
`workspace-views-menu.test.tsx`, `workflow-context-disclosure.test.tsx`, `scope-picker.test.tsx`,
`operating-scope-bar.test.tsx`, `app-shell.operating-scope.test.ts`, the
`lib/operating-scope/*.test.ts` suites (structure options, period presets, persistence, recent
scopes), `workspace-nav.test.tsx`, and `layout-switcher.test.tsx`.

Shared contract change (additive): `OperatingScopeValuesDto` (the operator's default scope) gained
optional `EntityId` and `FundId` members, appended last, so the server keeps entity and fund in a
saved default; the dashboard mirror is `types/operator-scope-preference.ts`. No endpoint was added.

React, React DOM, and their type packages are upgraded together. Runtime support uses the floating
Node major in `.nvmrc` and the minimum declared in this dashboard's `engines.node`; both prerequisite
doctors enforce that minimum. Account names, tab counts, and provider badges retain explicit text
separators so accessible names do not depend on CSS layout. StrictMode regression fixtures enable
the boundary at the rendering root and retain stale-response and financial-command assertions.

Report Run Detail exposes retained trial-balance account balances from the exact run's ledger
checkpoint through `/api/fund-structure/reporting/runs/{runId}/amounts`. Each balance opens the
shared `LedgerAmountProofDrawer` with its immutable `report:{runId}:{amountId}` subject and
retained fund/book/period. The panel validates tenant, company, fund, book, period and checkpoint
identity before exposing amounts; absent, ambiguous or foreign bindings remain blocked.
Exact journal-entry and ledger-entry IDs remain inspectable beside each amount. The drawer verifies
retained source digests and guarded scope links, and hides missing, altered or foreign evidence.
Run or scope changes abort prior requests and discard the previous selection. Reads prohibit
development fallback and never calculate support from current Accounting scope or live balances.
Focused coverage lives in `reporting-retained-amounts.test.tsx` and `report-amount-proof-api.test.ts`.

The reconciliation queue shows source observation and retained occurrence age directly. Clearing is labeled Source cleared and never implies case resolution. Legacy rows explicitly show identity/age unavailable. Queue refresh compares lineage metadata; inspector details include business and comparison scopes.

Accounting payment approvals provides an explicit entity-ID selection on the ordinary unscoped route. Unapplied scope edits disable decisions; applying a new entity clears the old review session. Cash Ladder retains the same-account scenario catalog through loading and errors while continuing to withhold stale balances and drill evidence.


Payment approvals forward the selected entity on pending-payment, bank-evidence, approval, and
rejection requests. Removing or changing entity scope clears the queue, decision notes and pending
confirmation; aborted reads or decisions cannot restore the previous entity's state. No entity
means no payment request. Fetch-level coverage lives in `banking-payments.api.test.ts`, alongside
scope-switch coverage in `accounting-screen.payment-approvals.test.tsx`.

Cash Ladder treats each account as a separate projection session. Account changes clear projection
balances, scenario, threshold edits, and drill selection immediately. Results and drill selections
also belong to the exact scenario/threshold request, so delayed or failed reads cannot present an
old projection as current evidence. `cash-ladder-screen.test.tsx` covers delayed switches, failures,
account round trips, and return to aggregate scope.

Historical price charts require a complete successful historical query before showing prices
or indicators. Failed and partial queries show an explicit incomplete state with failed source
paths and Retry; incomplete comparison series are withheld and named in an alert. The shared
API retains recovered bars and `fileFailures` for investigation without presenting them as a
complete series. Focused coverage lives in `historical-chart.test.tsx`.

Loan book accepts numeric and named host enums and preserves unknown values without inventing
USD. Server `currencyTotals` retain their denomination and an explicit no-FX notice; scalar
totals without a currency are withheld. Principal and undrawn amounts group by facility base
currency, while collateral retains its own appraisal currency. Each row also shows prepaid interest
and unapplied cash (PRD-115): amounts owed to the borrower, shown beside the receivables but not in
any total; a host that predates them shows the not-reported marker rather than zero.

The generated `api-routes.ts` catalog includes exact account-owned ledger-book oversight routes
for cash continuity, reviewed close prices and cash forecasts. Regenerate it from shared
`UiApiRoutes` and rebuild the tracked workstation bundle when those routes change; server-owned
scope and evidence verification are described in
[bounded account oversight](../../../docs/operators/account-oversight.md).

Accounting → Reconciliation includes an account-owned book oversight workbench with cash cases,
exact replay and owner handoffs, independently reviewed close-price versions, and dated cash/FX
forecasts. Supported handoff files receive a structured preview before capture. Explicit scope,
server capabilities, immutable history, source evidence and certification blockers stay visible.
API reads prohibit fixture fallback, financial values remain exact decimal strings, scope changes
abort old requests, and failed writes retain inputs or require history reload before retry.
Focused coverage lives in `accounting-screen.account-*.test.tsx` and `account-oversight.api.test.ts`.
Close-price capture retains its request identity and submitted command after an uncertain response.
After an authoritative history reload, the operator can explicitly recover or retry that same
capture; a new command cannot silently replace the unresolved action. An uncertain approval is
verified only on its original valuation date against the submitted reviewer and rationale; a
different retained approval keeps the lock and is reported as not the operator's own decision. A
cash comparison cannot be captured while an owner assignment draft is unsaved.

Accounting → Configuration includes an on-demand audit chain integrity check over
`GET /api/ledger/audit-integrity`. It reports each hash-chained log (accounting actions, ledger
events) as verified, broken with its failure kind and sequence, unavailable, or not kept in this
deployment, and never rolls an unchecked chain into a passing verdict. Leaving the screen aborts a
running check so the server releases the chain locks. Focused coverage lives in
`accounting-screen.audit-integrity*.test.ts(x)`.

Governed Journal Entry Detail exposes retained disposal tax results from the shared period and
journal read model. Operators can inspect mixed parcel character, carried holding-period starts,
economic and recognized gain/loss, deferred loss, and the exact retained policy revision. Settled,
provisional and missing-evidence results retain the server's reason, replacement-window end and
re-evaluation posture. Refresh reads retained evidence only; the browser never derives character,
settles an elapsed window, or recalculates tax amounts. Missing parcel allocation amounts stay
unknown. Reads prohibit development fallback and reject a different book, period or journal.
Focused coverage lives in `journal-entry-tax-results.test.tsx`,
`journal-entry-detail-screen.test.tsx` and `ledger-tax-results-api.test.ts`.

Tailwind and `@tailwindcss/postcss` are pinned to 4.3.3. The stylesheet uses the v4 import
with the retained TypeScript theme configuration, and compiled-style regression tests preserve
the workstation's utility behavior. Dependency updates must keep this pipeline and its built assets aligned.

Trading includes an account-scoped brokerage recovery panel backed by the shared readiness and
execution reconciliation projection. The operator can synchronize and reconcile Alpaca portfolio
evidence through the governed recovery endpoint. Connection health, portfolio currency and cash,
buying power, synchronization timestamps, completeness, impacted strategy runs, and retained-local
versus broker-order discrepancies remain inspectable. Missing, stale, inconsistent, failed, pending,
or wrong-account evidence stays visibly blocked; account changes abort and discard prior responses.
The active account polls shared readiness every ten seconds without overlapping requests.
Portfolio expiry uses the server's observation window and blocks locally even when a refresh fails;
repeated delivery of the same snapshot cannot extend its lifetime. Recovery never resubmits orders
or resumes runs. Focused coverage lives in
`trading-screen.brokerage-recovery.test.tsx`, `trading-screen.view-model.test.ts`, and `api.trading.test.ts`.

Trading's governed approvals panel shows each parked order's economic terms from the shared
escalation contract: routed notional (in place of the quantity for a dollar-sized order), stop and
trail terms, time in force, position intent, option contract, legs, and strategy. A term the server
sends as null gets no row; the fund account ID sits under Technical details. Coverage lives in
`governed-approvals-panel.test.tsx`.

Reporting run status and governed run detail expose an investment-income comparison over two
explicit retained run identities. Baseline options distinguish originally published and restated
results. The shared service retains the manifests, snapshot diff, explanation, and supporting
records; the browser never reattributes from live journals. Period, population, accounting basis,
currency, unsupported residuals, and incompatibility remain visible. Each contribution opens its
retained record in the shared accessible sheet. `incomeComparison` and `incomeContribution`
query parameters preserve the comparison and drill-through across reloads; selection parameters
retain both runs and the measure. Authoritative requests disable development fallback.
Monetary values stay canonical decimal strings from the API through display and residual checks;
the browser groups digits directly and rejects already-rounded JSON numbers.

Provider integration setup retains the server's immutable manifest version and digest after saving
or activating, and forwards that reference on later edits and activation. OpenAPI reimports retain
the same compare-and-set guard. Quarantine replay explicitly uses the original retained mapping;
applying an updated mapping requires an explicit remediation request with a selected version and digest.
Legacy evidence leaves unavailable provenance absent rather than deriving historical mappings in React.

Accounting's draft queue reads retained recurring occurrences through the shared scoped
`/api/ledger/journal-automation/recurring/occurrences` contract. Rows retain schedule/template
versions, draft identity, human approval status and source evidence references. Blocked rows
include period lock ownership and the governed reopen path. Failed reads stay unavailable;
the browser neither falls back to sample state nor infers approval or claims an occurrence.
Changing scope discards previous rows and ignores late responses. Focused coverage lives in
`RecurringJournalQueue.test.tsx` and `recurring-journals.api.test.ts`.

The stable test runner retains eight files per batch, two Vitest workers and process recycling.
Fresh JSON test evidence is written under `artifacts/test-results/browser/` at repository root.
Missing results, zero passing discovery or missing selected files fail the run. Counts, skips,
durations and stable test identity digests support the manual CI concurrency benchmark.

`EvidenceAmount` opens an exact retained evidence subject in the shared sheet primitive. Manual
journal detail debit/credit cells carry the entry and ledger-book identity; values without a supported
retained subject are never assigned inferred provenance. Reads disable development fallback and
show loading, unavailable, empty, stale and mismatched-subject states. Escape and focus restoration
use the shared sheet behavior. Full evidence links retain the selected ledger book.

Statement import links to the exact retained Operations Continuity workflow, book, and period.
An unavailable requested workflow fails closed. Close publication continues to require the full
fund/account/entity/book/period scope and current server readiness; the import handoff alone
does not supply that complete close authority.

Ledger Explorer clears the selected amount when its tab becomes inactive, its book or period
changes, or its posted journal no longer contains that amount. Returning to the prior scope
requires a new amount click; a late evidence response cannot restore the previous drawer.
`finance-standard-pages-screen.test.tsx` covers tab, book and period round trips and late responses.
Posted debits and credits render at the book currency's minor-unit precision (`$500.00`), the
Journal ID column links with the journal ID itself, and posting rows leave Status blank rather than
carrying instruction copy. Entities still show the retained `entityId`: the posted-journal DTO
carries no display name, so showing names needs a shared seam rather than a client-side guess.

Reconciliation break details expose source observation state (New, Aging, Cleared, Recurring),
lineage, occurrence and successful-run clearing independently of governed casework status.

The build lockfile resolves Browserslist 4.28.9, removing the high-severity cache-growth and
custom-stats parsing advisories reported by the September 6 production-certification scan.

Tailwind 4 uses its matching `@tailwindcss/postcss` adapter. The main stylesheet imports Tailwind
once, scoped with `source(none)`, and explicitly loads the existing `tailwind.config.ts` so
Meridian's semantic colors, typography, shadows and utility sources remain part of generated CSS.
Every config font stack ends in a CSS generic family (`monospace`, `sans-serif`),
providing an appropriate fallback if the bundled IBM Plex face is unavailable.
`design-system-contract.test.ts` guards these rules. The lockfile retains
patched `source-map-js` 1.2.2.

Screens share one status component, one formatting module, one table system and one type scale.
`scripts/design-system-ratchets.mjs` holds the remaining legacy uses in `src/screens/` to a
checked-in baseline that may only shrink: `npm run ratchets` reports the counts,
`npm run ratchets:update` lowers the baseline after a migration, and
`src/design-system/design-system-ratchets.test.ts` fails on any growth and on progress that has
not been recorded.

- **Status.** `StatusBadge` from `@/design-system/status` has five tones (ready, review, action,
  blocked, info), the same set WPF uses. `SeverityBadge` and `DesignSystemStatus` are aliases of
  it. Labels of one or two words are tracked caps; longer labels render in sentence case. Product
  maturity appears in the workspace About disclosure and the navigation tooltip, not as a badge.
  The `adHocStatusBadges` ratchet counts hand-styled chips. `@/design-system/status-styles` is
  the only status-to-look mapping: `toSeverity` reads any status, tone or badge variant onto the
  five severities, and `statusBadgeVariant`, `statusPanelClass`, `statusTextClass` and
  `statusMetricTone` give each severity one style. Screens do not keep local tone maps.
- **Formatting.** `@/lib/format` is the formatting module screens call instead of Intl. It
  provides `formatCount`, `formatMoney` (`accounting` puts negatives in parentheses at the
  currency's minor units, `trading` uses a minus sign at instrument precision, `summary` shows
  whole units), `formatQuantity`, and the timestamp helpers below. `formatDateTime` covers any
  other date layout through a pinned-locale `Intl.DateTimeFormat`. ESLint blocks `Intl.*` and
  `toLocale*String` in every non-test file under `src/screens/`, and the `directIntlFormatting`
  baseline is empty.
- **Timestamps.** One format for an instant: `formatTimestamp(value)` renders
  `Jun 30, 2026, 18:05 UTC` (month, day, year, 24-hour clock, zone label) in the operator's
  display time zone. The zone is UTC until the operator picks another IANA zone under Settings >
  Preferences > Appearance (`getDisplayTimeZone`/`setDisplayTimeZone`, persisted per browser in
  localStorage; an unknown or blocked value falls back to UTC). In a picked zone the label is the
  runtime's short name, e.g. `Jun 30, 2026, 14:05 EDT` or `Jul 1, 2026, 03:05 GMT+9`.
  - `precision: "second" | "millisecond"` adds to the same layout:
    `formatTimestamp(value, { precision: "second" })` gives `Jun 30, 2026, 18:05:07 UTC` (price
    alerts, journal tax results, brokerage recovery) and `"millisecond"` gives
    `Jun 30, 2026, 18:05:07.042 UTC` (live-quote ticks). The default is `"minute"`. There are no
    screen-local time formatters left; reporting schedule times use the default form too.
  - `formatRelativeTime(value, now)` gives `30s ago`, `5m ago`, `3d ago` or `in 2h`. Triage and
    Today queue ages use it through `TriageAge` (`components/meridian/operator-triage-queue.tsx`).
  - `<TimeText value display="absolute|relative" precision>` from `@/components/ui/time-text`
    renders one form inside `<time dateTime>` with the other in `title`, and re-renders when the
    zone changes. Plain-string call sites pick up a new zone on their next render.
  - `formatUtcTimestamp` (which takes the same `precision`), `formatUtcDate` and
    `formatUtcMonthYear` stay fixed to UTC for labels defined on the UTC clock (a UTC cut-off). An
    operator-facing time, including a retained record time such as the close-checklist "Recorded
    at" column, uses `formatTimestamp` or `<TimeText>`; its zone label keeps the header honest.
- **Calendar dates.** A trade, ex-, due, effective, or period date is carried as a `YYYY-MM-DD`
  string through `@/lib/local-date` (`LocalDate`, the native port of the Design System 4.3 carrier)
  and rendered with `LocalDateText` from `@/components/ui/local-date-text`, which emits
  `<time dateTime>`. Do not cut a day out of `toISOString()`: use `utcToday()` where the server
  evaluates the day in UTC and `LocalDate.dayOfInstant` for the operator's own day. Impossible days
  such as `2026-02-30` are null, never rolled into another day.
- **Number style.** One style for a financial figure, wherever it appears:
  - *Signed P&L*: `formatPnl` puts an explicit sign on every non-zero amount with thousands
    separators and fixed decimals (`+$1,234.56`, `-$1,234.56`, `$0.00`); `pnlToneForValue`
    gives the matching green/red tone, so sign and color never disagree. Other signed deltas
    pass `signed: true` to `formatNumber`, `formatPercent`, `formatRatioAsPercent`,
    `formatCount`, `formatAmountWithCode` or `formatMoney` (`+12.5%`, `-3`, `0`) instead of
    prepending a sign in the screen.
  - *Spelling*: profit and loss is "P&L" in every label. `normalizeFinancialLabel` rewrites
    "PnL", "P/L" and "P & L"; the stat strip, metric card, record explorer and proof drawer
    apply it to labels that arrive from a read model.
  - *Currency*: pass the ISO code whenever more than one currency is in scope
    (`formatMoney(value, { currency })`, `formatAmountWithCode` for a code suffix, or
    `formatCurrencyWithCode` for a symbol amount that keeps the code, e.g. `€100 EUR`).
  - *Decimals*: currency minor units for money, instrument precision for prices and
    quantities (`formatMoney`'s `decimals`, `formatQuantity`), whole units for a headline
    figure. Figures render in tabular numerals.
  - *Exact amounts*: `formatMoney` accepts a number or a decimal string. Both go through the
    exact-decimal core in `@/lib/exact-decimal` (`parseExactDecimal`, `toNumber`, `sumAmounts`),
    which rounds half away from zero and groups digits, so a server amount is never rounded
    through a float: `formatMoney("12345678901234.565")` is `$12,345,678,901,234.57`. An amount
    that rounds to zero is shown unsigned.
  - *One money formatter, two conventions*: `convention: "screen"` (the default) uses Intl
    symbols (`$`, `€`, `CA$`) and `-`. `convention: "statement"` uses the statement symbol
    table (`currencySymbol`) and U+2212 `−`, for ledgers, statements and journal grids. `parens`,
    `zeroDash` and `signed` work under either convention. `@/components/accounting/money` is a
    thin statement-convention wrapper: two decimals, no symbol unless a currency is given, and
    unparseable text passed through as is.
  - *Enforcement*: two ratchets keep screens on `@/lib/format`, and both baselines are empty,
    so a new occurrence fails the ratchet test. `handRolledNumberFormatting` counts `.toFixed(`
    in screens, which skips separators and the sign convention: a displayed figure with fixed
    decimals uses `formatDecimal(value, digits)` (or `formatPercent`, `formatRatioAsPercent`,
    `formatMoney`), and an SVG coordinate or CSS length uses the ungrouped `formatFixed`.
    `localMoneyFormatters` counts money and P&L formatters defined in a screen instead of
    imported from `@/lib/format`.
- **Headline figures.** `StatStrip` and `MetricCard` apply the rules in `@/lib/metric-trust`
  to every `MetricSnapshot`, so a screen cannot opt out by accident:
  - *Source*: a figure states where it comes from. `provenance` carries source, as-of time
    and scope; the strip prints what its figures share once under the row. When figures in one
    strip come from different sources it names each source with the figures it supplies
    ("Active runs, Open positions from System overview; Providers healthy from Cross-screen
    counts · As of …"). A strip fed by the workstation load passes `source="…"` and
    `figureSource="…"` (the payload that supplies its figures) and takes that payload's evidence
    from the shell (`WorkspaceFigureContextProvider`). Evidence is kept per payload
    (`figureSourceLoadedAt` and `figureSourcesStale` in `useWorkstationData`), never per refresh:
    the as-of time is when that payload last loaded, not the time of the latest refresh, which
    may have reloaded another workspace or nothing. A figure another payload supplies carries
    its own `provenance` and `freshness` (Settings' provider health comes from the cross-screen
    counts, not the overview), and a strip that names no payload states no as-of time. A screen
    with its own freshness evidence stamps its metrics with `withMetricContext`, still dated
    from the payload that supplies the values (`resolveFigureEvidence`): Trading's totals follow
    the Trading payload, not a newer brokerage observation. Their scope is the fund account that
    payload was loaded for (`figureSourceScope`, `resolveFigureScope`), not the selected one:
    after an account switch the previous account's figures stay labelled as that account's, and
    stale, until the new payload loads.
  - *Change*: a bare change (`+8%`, `0%`, `+2`) is shown only with the `baseline` it is
    measured against (`+8% vs prior session`) and is dropped otherwise. A change that names
    its comparison (`3 new since your last visit`) and descriptive captions pass through.
  - *Basis*: a strip prints the basis its figures share once, and nothing when no figure
    declares a basis or an as-of time (`hasMetricBasisDetail`). Cards omit an undeclared basis;
    the figure passport says "Basis not declared by source" (`metricBasisLabel`).
  - *Freshness*: `freshness: "stale" | "unverified"` mutes the figure, adds a clock, marks
    the strip "Stale figures"/"Unverified figures" and explains the age on hover and to
    assistive technology. Staleness is decided per payload: a payload whose own reload failed
    marks just the figures it supplies, while the ones that did reload stay fresh, whatever the
    refresh as a whole reported. It stays stale until that payload reloads; refreshing another
    workspace clears the shell's error map but not this. An unconfirmed session keeps every
    figure unverified, and a screen's own evidence (Trading's broker observation) can lower a
    figure's verdict but never raise it (`combineMetricFreshness`). Portfolio's exposure and
    unrealized P&L are sums over positions, so they are unverified, with a caption such as
    "2 of 5 marks need review", while any position's mark is not accepted as current
    (`withPortfolioMarkEvidence`): a recent load does not make a total better evidenced than
    its inputs. Portfolio's totals go stale when the payload that
    supplies them is listed or its load is past the freshness budget. Outside the shell, where no
    per-payload record exists, the Portfolio poll settling as `partial` is the evidence (its
    Portfolio request was rejected; a failing side feed such as an unconnected brokerage does
    not count).
  - *Disagreement*: when two panels on one page report different values for the same
    quantity, `buildFigureDifferenceNote` and `FigureDifferenceNote` show "Why these differ"
    with both values and each panel's source. Portfolio uses it for the header strip and the
    Portfolio Explorer. Give a figure a `basis` when panels can measure it differently (the
    header's exposure is gross, the explorer's is net): a gross-against-net gap is not reported
    by itself, and is labelled with both bases when listed beside a like-for-like difference.
- **Detail panes.** `DetailPane` (`components/meridian/detail-pane.tsx`) is the pane for a
  selected table row:
  - *One heading line*: object type, name and one status badge. There is no separate
    "Selected from …" pill, "Detail" chip or status eyebrow; the shared stylesheet no longer
    draws the pill for any panel.
  - *Field rows*: `DetailFieldItem` puts a label beside a left-aligned value and lets the value
    wrap under its label once the row is narrower than about 290px, so a value is never squeezed
    into a column too narrow for a word. `DetailFields` renders a list of them from raw
    `{ label, value, tone }` rows; `columns="auto"` flows a wide list into columns.
  - *Typesetting* (`detail-pane.view-model.ts`): figures and identifiers use the data face,
    phrases and written-out dates the body face; figures and dates stay on one line; prose wraps
    at word boundaries only.
  - *Identifiers*: GUID- and digest-shaped values, and rows marked `technical`, move to a
    "Technical details" disclosure with a copy button.
  - *No repetition*: pass `shownInRow` with the column labels the selected row always shows;
    the pane lists what the row does not, plus the next action in `actions`.
  - *Shared panes*: two tables may control one pane (Trading orders and fills do); the pane
    shows the table selected last, and Escape returns focus to that table's selected row.
  `EntitySummary` applies the same heading line and rows, so panels built on it follow the rules
  without changes at the call site.
- **Tables.** `DenseDataTable` (`components/meridian/ui-kit-primitives.tsx`) provides:
  - a sticky header inside a bounded scroll area;
  - `aria-sort` sorting, either built in from a column's `sortValue` (numbers start
    largest-first, text A–Z, missing values last) or owned by the caller through `sort` and
    `onToggleSort`;
  - `numeric` columns, right-aligned with tabular figures;
  - `hideable` and `defaultHidden` columns behind a Columns menu, remembered per `tableId`, or
    owned by the caller through `hiddenColumnIds` and `onHiddenColumnIdsChange` when a link or
    saved view must carry them;
  - a per-table `density` override. Otherwise rows follow the operator density chosen in
    Settings, which `main.tsx` re-applies at startup.

  Grouped tables whose posting lines span columns, such as the Ledger Explorer, keep their own
  rows inside a `dense-data-table` and use `DenseDataTableHeaderCell`, `nextDenseSort` and
  `sortRowsByValue`. Sorting uses only raw values. Tables whose API rows carry preformatted
  figures (trading and portfolio positions, orders) sort on their text columns, and the
  reconciliation queue keeps its own sort control. The `rawTables` ratchet counts hand-built
  tables.
- **Cross-screen counts.** Open orders, provider health and open breaks come from one selector,
  `lib/cross-screen-counts.ts`. A screen holding the source list (the blotter, the Data provider
  rows, the break queue) counts it with the shared definition; Settings and the shell read the
  server's `/api/workstation/cross-screen-counts`, loaded on every route. Headline cards for these
  counts take the selector's value and drop their basis-less delta, and an unreadable count shows
  "—". Settings, which does not refresh the Data payload, prefers the shared provider count over
  retained Data rows. `lib/cross-screen-counts.screens.test.ts` asserts Trading, Settings, Data and Accounting
  agree from one fixture set. Accounting takes open breaks from the payload's `workspace.openBreaks`,
  which the server counts over the whole queue even when it withholds `breakQueue` from a caller
  who may see the count but not the cases.
- **Operator copy.** Records are named by display name. GUID-shaped identifiers stay out of the
  reading path: `lib/identifiers.ts` (`isOpaqueIdentifier`, `withoutOpaqueIdentifiers`) strips
  them from headings, and the identifier lives in a `TechnicalDetails` disclosure, a `title`
  attribute or a read-only scope field. Counts with a noun go through `pluralizeCount` from `@/lib/format`. A
  position with no shared mark assessment shows one "No mark observed" hint (`lib/mark-freshness.ts`)
  instead of a row per unknown field. The screenshot harness checks every route's rendered main
  content for both rules (`findCaptureContentIssues` in
  `scripts/dev/capture-web-screenshots.mjs`) and fails the capture; a route that cannot comply yet
  lists the rule with a reason under `allowedContentIssues` in
  `scripts/dev/web-screenshot-routes.json`. Operations Continuity is the one exception today: its
  shared summary identifies a close workflow by fund-account identifier only.
- **Operator language.** The same harness check fails a route whose main content shows a request
  path (`/api/...`), the words "lane", "payload", "mutation" or "caller-visible", or a slug-shaped
  record identifier (`run-dev-1`, `default-fund`, `entity-fund-alpha`; `isSlugIdentifier` keeps
  ordinary compounds such as "as-of" out). Twelve routes whose read models name records only by
  key, the three Settings diagnostics pages that list backend routes, and Operations Continuity's
  server-written "lane" copy are excused with reasons in the route catalog.
- **Failed requests.** `ApiError.message` is operator copy: the reason the service gave, or a plain
  sentence for the status. It never contains the request path or status code, so the many call
  sites that render `error.message` cannot leak a route. `presentApiError` (`lib/api-errors.ts`)
  returns the sentence, whether Retry can help (408, 429, 5xx and non-HTTP failures), and the
  status, request path and correlation identifier as technical details; `ErrorNotice`
  (`components/ui/error-notice.tsx`) renders them with the technical details collapsed. New error
  states use `ErrorNotice`; the Recurring journals queue is the first.
- **Disabled buttons.** Every `Button` variant shares one disabled look from `styles/index.css`:
  neutral fill, dashed edge and muted label at full opacity (`--ws-disabled-*` tokens; label
  4.8:1, edge at least 3:1 on every surface, asserted in `components/ui/button.test.tsx`). A busy
  button keeps its own colours. Pass `disabledReason` whenever a button can be disabled; the
  harness fails a route that renders a disabled primary button without one.
- **Destructive bulk actions.** `OverflowMenu` (`components/ui/overflow-menu.tsx`) holds actions
  that should not sit beside a primary action. Trading's "Cancel all open orders…" lives there,
  shows why it is unavailable when there are no open orders, and still opens the confirmation step.
- **Status agreement.** One rule (`src/lib/status-signals.ts`) classifies, ranks and summarizes
  every status fact, and one shell attention state (`src/app-shell.attention.ts`) feeds the
  masthead attention pill, every page status row, Today's status row and the status bar. The
  same input therefore gives the same status on each surface; see "Status signals" below. The
  status bar's Sync item reports the workstation's own read of the API. The masthead pill is the
  one shell place that names the worst issue: the status bar and page status rows do not repeat
  it (UX consolidation P1). The Daily Control Tower's
  Evidence line names only the event correlated with the leading decision's queue row; with none
  it says so and cites the latest event as workstation context.
- **Page freshness in the status bar.** The shell attention state covers shell-wide sources; the
  freshness of the data on a page belongs to that page's status row. `OperationalTrustSummary`
  reports the freshness fact it renders to the shell (`lib/page-trust-context.tsx`), and when
  that fact needs review or is blocked the bar says "Sync connected" plus a Freshness item in the
  row's own words instead of "up to date". Trading and Portfolio label a brokerage account
  "Account", so the row does not state a second "scope" beside the header's fund picker. The
  capture harness's `status-contradiction` rule checks every route, reading the `data-trust-fact`
  and `data-trust-tone` attributes `StatusRow` puts on each named issue.
- **Operating scope.** Copy link appends every scope dimension the current route did not write
  into its URL (`buildShareableLink`), so a link from any screen restores the sender's scope in a
  fresh browser. The scope picker offers the last five applied scopes (per browser) and the
  operator's default scope from `/api/workstation/operator/scope-preference`; choosing either only
  loads the draft, and Apply stays the one action that changes scope. The default is applied only
  when a session opens with no scope in its link or browser. The header `OperatingScopeBar` shows
  each dimension as a segment on every workspace, marked applied or retained.
- **Saved views.** The positions, break queue and ledger tables keep their filters, sort and
  hidden columns in the URL: positions in the `view` envelope, breaks in `reconciliationSearch`,
  `reconciliationStatus`, `reconciliationSort` and `reconciliationColumns`, the ledger in
  `ledgerSearch` and `ledgerSort` (`columnId:direction`). A saved layout captures the route with
  those parameters, so restoring it restores the table, and the command palette offers each
  saved layout as "Open saved view …". `lib/shareable-view.test.ts` restores each table from a
  copied link with empty browser storage.
- **Bulk break triage.** The break queue's row checkboxes build a bulk selection, limited to the
  breaks the current filters show. Assign to me, Route to owner and Resolve with reason go through
  the governed casework endpoints: a dry run checks the whole selection and lists each blocked
  break with its reason, and the operator then confirms an execute of only the breaks that passed,
  applied all or nothing. The server stamps the actor (Assign to me sets
  `assignToCurrentOperator`, so it names the login user, not the display name) and writes an
  audit entry for each case. Resolve takes a reason, a root cause and a resolution from the
  taxonomy endpoint, plus an optional evidence reference.
- **Since your last visit.** `lib/since-last-visit.ts` records the operator's visit to the break
  queue (`POST /api/workstation/operator/visits/reconciliation-breaks`) once per browser tab. The
  visit it replaces is the baseline: a break detected after it is marked New, a break whose
  activity (`lastUpdatedAt`) moved after it is marked Updated, and the queue states the counts and
  the baseline time. The Accounting open-breaks card reads the same baseline for its delta ("2 new
  since your last visit") without recording a visit, so only opening the break queue advances it. When the visit cannot be recorded, nothing is marked. The tab cache
  keeps the baseline with the visit it recorded; because session storage outlives a sign-out, a
  later page load first reads the operator's last visit (`GET`, which records nothing) and reuses
  the cache only when that is still this tab's visit.
- **Queue keys.** `lib/queue-keyboard-map.ts` holds the break queue's key map; the handler, the
  `?` shortcut sheet and this table come from it, and `lib/queue-keyboard-map.test.ts` checks the
  table against the map. Keys work while focus is in the queue and not in a text field. The queue
  claims them before a focused row's type-ahead, so other letters still jump to a matching row.

  | Key | Action |
  | --- | --- |
  | `j` | Move to the next break |
  | `k` | Move to the previous break |
  | `x` | Add the current break to the bulk selection, or remove it |
  | `a` | Assign to me: the bulk selection when there is one, otherwise the current break |
  | `e` | Open the evidence drawer for the current break |
  | `?` | Show these shortcuts |
- **Type scale.** There are three tiers: `type-page-title` (the workspace header's h1),
  `type-section` (CardTitle and region headings) and `type-label`. A label of one or two words may
  add `type-label--caps`. Longer labels render in sentence case: `FormRow`,
  `FormSectionLabel`, `Label`, `ToolbarStrip` and `EntitySummary` decide per rendered label,
  and eyebrows use `eyebrowClassName()` or the `eyebrow-label--sentence` modifier. Prose uses the body face and mono is reserved for figures
  and identifiers; `EntitySummary` values choose a face with `isDataLikeValue`. Each region has at
  most one primary button.

First launch is browser-primary. `/setup` renders the first-run concierge while the
shared first-run API remains the source of truth for starter kits, sample safety labels,
recommendations, and completed activation outcomes. Sample mode stays offline-capable
and visibly labelled `SAMPLE · PAPER` throughout the shell. Until activation status is known, the
normal shell stays closed and a failed status read exposes a retry instead of assuming setup is complete.
The first-run concierge also exposes an accessible error and Retry when it loads status itself;
retry shows loading progress and leaving the screen cancels the pending read. Accounting money
helpers preserve statement trailing-minus signs when formatting and balancing amounts.

After setup the masthead `Getting started n/m` chip opens the activation checklist, which lists
every outcome the host tracks and routes to the surface that completes the next one. Completion is
reported by the surface that did the work -- statement import commit, reconciliation break
resolution, report run, and analysis export each call `recordActivationOutcome` in
`src/lib/first-run/activation.ts` -- so the count never advances on a page visit alone. Once every
outcome is complete the chip leaves the masthead (it stays while its checklist sheet is open); the
host keeps the completion record, and the sample-workspace badge and desktop launch remain.

Strategy's PlotTool never substitutes literal values for run-linked metrics: a missing Net P&L,
total return, Sharpe ratio or max drawdown renders `Unavailable` with a neutral tone.

Quant Lab plots preserve the worker's calculated series, including fractional cumulative returns
and drawdowns, without repeating financial transformations in the browser. Notebook execution
forwards the selected symbol, date range and interval using the worker's `context.*` parameters;
cell outputs retain emitted charts and trades alongside console, metrics and diagnostics. The
server-shaped regression fixture lives in `src/test/fixtures/quant-run-response.json`.

## Shared close and lot convergence

Accounting forwards the entity along with fund/book/account/period when requesting the shared close decision. The close headline requires a complete ready server projection. Legacy metrics remain diagnostic and cannot produce Ready when the shared service is absent. Focused proof: `accounting-screen.close-cockpit.view-model.test.ts`.

Portfolio and Trading positions display the shared mark observation date, age, and assessment. Missing assessments remain review required; recorded amounts do not imply current approved support. The valuation preview shows affected positions before a draft is requested. Both workstations retain the server close decision through blocked-to-ready recovery instead of calculating a separate readiness score.

Accounting and Operations Continuity require the shared decision to match all five selected close dimensions and the current workflow revision. Scope changes invalidate prior decisions and in-flight responses. Hard-lock requests forward that explicit scope to server-side close validation; a previously ready response cannot authorize a newly selected subject.

Operations Continuity refreshes the selected workflow detail on every workflow-list refresh and
close-scope change, including repairs that retain the workflow version but replace report evidence.
Publication waits for matching summary/detail revisions and the refreshed shared decision. The
decision's workflow must match the selected report pack and publication evidence identities (ID,
source, route and capture time), including repairs that retain the workflow version. Failed,
mismatched or superseded detail reads remain blocked, and late responses cannot restore old controls
or evidence. Shared services still validate current close-plan and report-evidence versions.
Focused proof: `operations-continuity-screen.test.tsx` and `operations-continuity-screen.view-model.test.ts`.

Operations Continuity submits checklist controls from the shared workflow's explicit acknowledgment actor and time, including the first submission before any close package exists. Missing controls block submission. Rejected or reopened workflows can submit a newly reviewed cycle without reusing old package approvals. Approval decisions carry retained submission evidence; an assigned reviewer is not counted as having approved until the server records the actual decision. Close publication uses the current submission and decision history together with current prerequisite acknowledgments.
Focused proof: `operations-continuity-screen.view-model.test.ts` and `operations-continuity-screen.test.tsx`.

## Task-centered workbenches

The browser workspaces retain the seven root destinations and existing route ownership. Strategy
now leads with retained runs; Data leads with loaded historical repair jobs; Settings leads with a
searchable task directory. Authoring, intake, connections, exports, and analytics tools remain in
labelled disclosures and their existing dedicated routes. These lists are presentation-only; they
do not calculate financial readiness or grant authority.

`WorkspaceTaskWorkbench` composes the existing `WorkspaceWorkbenchShell`, `DenseDataTable`, and
`DenseRowDetailPanel`. Its item contract carries an ID, title, description, destination, action
label, optional context text, and optional supplied facts. Context text is not a readiness claim.
The row action provides direct navigation; the inspector keeps the same action next to its facts.
Use domain-specific workbenches for mutations, approvals, or workflows with specialized selection
semantics. Do not turn this directory into an aggregated financial queue.

Selection and search are URL-backed (`strategyRun`, `dataJob`, `settingsTask`, and the corresponding
`Search` suffix; tool directories use `strategyTask` and `dataTask`). Search replaces history;
selection creates an entry. Filtering preserves the selected inspector. An explicit unavailable
ID stays unavailable instead of selecting another record. Clearing search retains selection.
Destination links preserve query scope, unrelated parameters, and the hash, with explicit target
parameters taking precedence. Existing Portfolio and Reporting selection formats remain unchanged.

The shell's additive `switchInspector` option switches between list and inspector below 1280 CSS
pixels. Inspect selection opens the current inspector; Back to work list returns focus to the
selected row, falling back to the work surface when the row is absent. Enter/Space on a row reveals
its detail target, and Escape restores the list. Existing proof hash links reveal and focus their
inspector target on initial load, history navigation, and repeated activation of the same hash.
Wider screens retain adjacent, independently
scrollable inspection. Use `inspectorRole="region"` inside existing landmarks. Accounting
reconciliation retains its specialized queue/history/scroll and recovery behavior.

Portfolio, Trading positions/orders, and Reporting daily review reuse the shell. Portfolio's asset
action precedes supporting facts. Trading keeps recovery commands, live blockers, and failure
reasons outside the Brokerage recovery evidence disclosure. The shell provenance notice remains
persistent and explicit, using less vertical space by placing its explanation alongside its title
when space allows. Theme and density preferences remain authoritative.

Focused coverage: `workspace-task-workbench.test.tsx`, existing shell/Reporting tests, Portfolio
positions controls, and Strategy/Data/Settings/Trading screen tests. Browser acceptance includes
all seven workspaces at 1920×1080, 1440×900, and 1366×768, plus queue/inspector switching at
1100 CSS pixels. Validate production-connected permissions and writes separately from fixture QA.

## Work-first Portfolio and Reporting

Portfolio opens on selectable holdings before the supporting retained-record explorer and
readiness handoff. Reporting opens on its selectable daily work queue before period production
tools. Their compact data-confidence disclosures keep source, scope, freshness, completeness,
and blockers visible; expanding them reveals explanations and recovery actions. The existing
native disclosure pattern also retains full Portfolio readiness and Reporting production tools.

Reporting queue selection uses `reportingWork` in the URL, preserving other query parameters
and the hash. The selected item owns the inspector's evidence and actions. Removed or unknown
items are explicitly unavailable, and changing financial scope clears the prior selection.
The shared dense-row keyboard contract supports row selection, detail focus, and return focus.

`src/lib/workstation-features.ts` supplies wired browser destinations to both sidebar navigation
and local command search. Purpose descriptions and search-only aliases expose intents such as
cash forecast, statement import, historical prices, and running reports. Local destinations remain
searchable when remote command or entity search is unavailable; unwired routes stay excluded.
Accounting onboarding searches open the existing External GL surface, which hosts the bounded
onboarding workspace and its retained comparison and readiness evidence. Its shadow-mode section
(`accounting-screen.onboarding-shadow-panel.tsx`) covers shadow-book designation, incumbent
opening-balance upload and report, per-period agreement, difference explanations, the parallel-run
certificate and promotion; tables use the shared `DenseDataTable`.
From 1200px the masthead is one 48px row: the environment badge leads with the never-shrinking
session environment and adds the data qualifier only when the data is not confirmed real
(`PAPER · SEEDED`; LIVE uses the alarm-red `--live-env` token and follows the resolved session, not
the workspace bootstrap), the attention pill names the worst open issue, and desktop launch is an
icon button with the same accessible name. Between 981px and 1199px operator controls take a second
row so search stays readable.

### Status signals

The shell shows status in four places, all derived from one attention state so they cannot
disagree:

- **Environment badge** (masthead): `PAPER`, `LIVE` or `DEMO`, plus `SEEDED`, `SIMULATED`,
  `SAMPLE` or `UNKNOWN` when the data is not confirmed real. It says what the operator is looking
  at and never changes colour for an issue. A first-run sample workspace is folded into the same
  label, so there is no separate sample-data chip. Build, environment, provenance and provider
  facts are in its disclosure.
- **Attention pill** (masthead): the worst open issue across the workstation with a count of the
  rest, or `All clear`. It always opens Today. The count matches the queue count beside `Today`
  in the sidebar; a provider warning or failed workspace area that is also queued is counted once.
- **Page status row** (`OperationalTrustSummary`, rendered by `StatusRow`): one line that reads
  `All sources current` or names only the facts that are not ready
  (`Scope: none selected`, `Freshness: 187d stale`). The per-fact breakdown, with detail and
  recovery actions, is behind `Details`. Shell source issues (provider posture, unverified
  provenance, workspace data that did not load, degraded host) are the pill's to name: the row
  lists them only under `Details` (`contextFacts`), and when the page's own facts are ready it
  reads `This page's sources are current · N workspace issues in the trust strip`, so it never
  says `All sources current` under a warning pill and never repeats the pill's items.
- **Status bar** (footer): Sync and the page's Freshness only; it never repeats the pill's issue.

Rules worth knowing when adding a signal:

- Anything short of `ready` is an issue and is named; `ready` facts are never listed in a summary
  line. Blocked outranks review, which outranks pending/unknown.
- Declared demo data on a Paper or Demo session is not an issue (the badge names it). Provenance
  that was never confirmed needs review; non-real data in a LIVE session blocks.
- LIVE is identity, not a warning.
- Any request failure the status bar's Sync item reports as an error is a blocked `Workspace data`
  signal: a workspace area that did not load, or a bootstrap request (session, host status, shared
  counts) that failed while the workspaces loaded. The pill never reads `All clear` beside a Sync
  error. A missing workflow catalog alone needs review.
- The leading decision is the worst queued item by tone and does not depend on the route, even
  though Today lists its queue finance-first.
- New shell-wide signals belong in `buildAppShellAttentionState`; page-specific facts belong in the
  page's `OperationalTrustSummary`. Do not add a status indicator that computes its own tone.

Workspace routes open on their own job: the route heading leads, the workspace KPI strip follows it,
and the sidebar is the only sub-route navigation (Trading, Portfolio, Strategy and Data no longer
repeat it as an in-page tab strip; Data's Backfill queue has its own sidebar entry). Off the Trading
overview the brokerage recovery panel is one blocked-state row with a Review link to the full panel,
and nothing once evidence reconciles. With no fund account in scope and a brokerage environment that
is known and not live, the panel and row are a neutral setup state with a "Choose account" action;
a live, unknown or failed state keeps the blocked row. The sidebar pins `Today` (the Daily Control
Tower) above the seven workspaces and shows per-workspace attention counts from
`selectDailyControlTowerQueueItems`, the same selection the Control Tower lists as the workspace-signal
rows of its one Today queue grid (shared inbox rows in that grid are not counted); each count is part of its link's accessible name, the badge sits beside
the link so the visible label stays in the name, and the link's tooltip names the unit
(`3 items from Accounting in Today's finance queue`).

## Purpose

Accounting balances and materiality labels format amounts using the supplied currency code, including signed values. Currency codes appear once even when the formatter emits the code as its symbol, and negative zero renders as zero.

Browser workstation dashboard is the active browser operator workstation.

## Layer responsibility

This module owns the browser UI source for operator workflows. Keep shared contracts and read-model
logic in `src/Meridian.Ui.Shared` or `src/Meridian.Ui.Services` when the same behavior is consumed
by desktop or host surfaces.

The PRD-109 contract parity gate enforces portfolio and position ownership scope, portfolio
accounting policy and source provenance, and the execution mode on fill and attribution summaries.
Keep the TypeScript declarations aligned with `StrategyRunReadModels.cs`; nullable source
references remain nullable, while required policy and mode fields must also be present in fixtures.

The browser workstation visual system uses Meridian Design System 4.3.0's
paper/plate/well hierarchy, graphite chrome and primary actions, independent blue
focus/selection/link roles, terracotta brand/scenario signal, and local IBM Plex
Sans/Mono typography. Light and dark appearances resolve through semantic roles. Keep
visual changes on the shared tokens and primitives in `src/styles/index.css` and `src/components/ui/`
instead of introducing one-off screen styling.

## Key folders and files

- `src/` - React/TypeScript workstation source.
- `src/app-shell.command-palette.ts` - app-shell command-palette trigger and keyboard shortcut view models.
- `src/app-shell.development-fixture-notice.ts` - app-shell no-host demo-data notice and evidence path view models.
- `src/app-shell.route-focus.ts` - app-shell route announcement, document title, and hash-target focus view models.
- `src/app-shell.status-panel.ts` - app-shell bootstrap, degraded workspace, and recovery status view models.
- `src/app-shell.trust-strip.ts` - app-shell environment badge plus build, mode, source, and provider posture view models.
- `src/app-shell.attention.ts` - the single shell attention state behind the masthead pill, page status rows, Today, and the status bar.
- `src/lib/status-signals.ts` - the one rule that ranks status tones and summarizes a set of status facts.
- `src/app-shell.workflow-continuity-types.ts` - shell workflow-continuity view model contract.
- `src/screens/operations-continuity-screen.date-format.ts` - retained UTC timestamp and due-date formatting used by the Operations Continuity view model.
- `src/components/ui/` - shared Meridian Design System primitives, including buttons, inputs, selects, badges, tooltips, dialogs/modals, sheets, checkbox/toggle, breadcrumb, form rows/grids, tabs, status banners, context menus, multi-select, toast, and panel surfaces.
- `src/design-system/assets.ts` - dashboard bridge for the checked-in `Meridian Design System/` package, centralizing brand and workspace icon imports before app-shell or navigation components consume them.
- `src/assets/` - browser-bundled brand and icon copies from the `Meridian Design System/assets/` source package, including the app icon and PNG tile.
- `src/types.ts` - compatibility barrel for browser DTO mirrors. Add new domain-specific DTO mirrors under `src/types/` and re-export them from this file instead of growing the barrel directly.
- `src/types/corporate-action-inbox.ts` - corporate-action source proposals, durable case projections and retained election cutoffs; `workstation-7.ts` keeps compatible named type exports.
- `src/lib/dev-fixtures.ts` - compatibility facade for no-host fixtures. Add new screen or domain fixture payloads under `src/lib/dev-fixtures/` and register them through the resolver map instead of adding another large block to the facade.
- `src/scenarios/` - typed response scenarios shared by URL previews, Vitest, and Playwright screenshots. `accounting-payload.ts` owns the accounting payload; `accounting.ts` owns its normal, empty, delayed, forbidden, and failed variants.
- `src/test/scenarios.ts` - strict scenario installation and request coverage assertions for Vitest.
- `package.json` - dashboard build, test, and tooling commands.
- Test files - browser workflow and component coverage.

Legacy `/overview/*` links remain compatibility redirects in the app shell. The retired overview
screen, Today panel, and unrouted Settings admin operations console are recorded as comment-only
tombstones under `archive/code/src/Meridian.Ui/dashboard/src/screens/` until the 2026-09-11 archive cleanup (`982eea2d`) removed `archive/code/`; [the last copies](https://github.com/rodoHasArrived/Meridian-main/tree/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/code/src/Meridian.Ui/dashboard/src/screens) remain in history.


## Chart accessibility contract

Strategy equity-path comparison is composed by `strategy-screen.equity-paths.tsx` and
`strategy-screen.equity-paths.view-model.ts`; the run-library view model retains its existing
exports. The chart set remains separate from Compare, Diff and Promote selection.

The SVG charts in `src/components/charts/` (`CandleChart`, `EquityCurve`, `DrawdownChart`,
`Histogram`, `DepthChart`) render through the shared `ChartSvg` frame in `chart-a11y.tsx` and
accept three optional props:

- `ariaLabel` names the graphic. Each chart defaults to a generic type label, so screens should
  pass a specific one (run, symbol, or study) and must not wrap the chart in their own
  `role="img"` element.
- `ariaDescription` carries the takeaway (range, extremes, final values) as the accessible
  description.
- `accessibleData` (`{ caption?, columns, rows }`) renders a visually hidden table beside the
  graphic. Pass a memoized reference for long series; skip it when the same values are already
  on screen as a table or list.

Charts with crosshair callbacks render the keyboard slider from `chart-interaction.tsx` as a
sibling of the image inside a `role="group"` svg, never inside `role="img"`, and the slider shows
the `--focus-ring` outline on keyboard focus. Coverage lives in `charts.test.tsx` and the
Portfolio, covered-call, Live Quotes, and Strategy screen tests.


## Dense row detail accessibility contract

Dense row lists that drive an adjacent detail panel must use the shared contract in
`src/components/meridian/dense-row-detail-accessibility.tsx` through `DenseDataTable` and
`DenseRowDetailPanel` instead of screen-local keyboard or ARIA implementations.

The contract standardizes:

- Arrow Up/Down, Home, and End as row focus plus selection movement.
- Enter and Space as row activation that selects the focused row and hands focus to the controlled
  detail panel.
- Escape from a detail panel as the return path to the selected controlling row.
- `aria-selected`, `aria-expanded`, and `aria-controls` on selectable rows, with labelled,
  programmatically focusable detail regions that announce updates politely.
- A table-scoped live region that announces selection changes and panel refreshes.

Current dense-row detail consumers covered by regression tests include Portfolio positions,
Portfolio run evidence, Trading recent fills, Data backfill queue rows, Data export rows, and
Security Master lots. The Daily Control Tower's Today queue uses the same table/inspector contract,
including row selection, focus handoff, and Escape return behavior: a workspace-signal row opens its
evidence rail and a shared-inbox row opens its triage rail.

The Data backfill workstream reads `/api/backfill/executions` as the durable remediation evidence
source. Its remediation SLA queue keeps server-owned tier, deadline, status, provider, workflow,
owner-assignment, outcome, and compatibility-derived provenance visible, with operator sorting on
SLA tier and deadline. Live provider-attempt progress remains a separate bounded projection so a
dropped transient notification cannot erase the retained execution/SLA record.

The workbench owns its scrollport and the containing block for absolute accessibility text.
Off-screen table captions stay available to assistive technology without expanding the outer
document into a blank page. Desktop workstations retain internal scrolling; the existing zoom
layout retains page scrolling.

## Important workflows

From the repository root, use `npm run dev` for the seeded backend with watch/restarts and Vite
hot reload, or `npm run dev:fixtures` for explicit fixture-only responses without a backend.
Backend-connected errors never fall back to fixtures. See the
[browser development launcher](../../../docs/engineering/web-development.md) for prerequisites,
port options, readiness, shutdown, and manual acceptance checks.

For a deterministic accounting preview, open
`http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.normal`
in a fixture-only session. The [shared browser scenario guide](../../../docs/development/browser-scenarios.md)
lists all five selectors and explicit HTTP statuses, strict test behavior, and screenshot commands.
Changing the first reconciliation queue row's `strategyName` in `src/scenarios/accounting-payload.ts`
updates the preview, production-component Vitest expectations, and screenshot readiness from one edit.
Screenshot scenario selection keeps the selected route's headings and selector checks; populated
Approval Inbox captures keep approval readiness instead of waiting for reconciliation strategy names.

The browser workstation exposes `/accounting/entity-setup` for the shared fund-structure setup wizard. The feature posts drafts to `/api/fund-structure/setup-drafts/validate` for validation and preview, then `/api/fund-structure/setup-drafts/create` for review-and-create instead of reimplementing setup orchestration in React.


This is the active operator UI lane; keep shared contract compatibility with retained WPF consumers. Security
Master Governance detail uses the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` projections for cash-flow schedules, factor provenance, and open-lot exposure
review. For a Bond that declares a call or put schedule the server sends `Call`, `Put`,
`CallNoticeDeadline` and `PutNoticeDeadline` schedule rows; the last three have no label entry and are
shown by their type name, and a notice-deadline row has no pay date, so its effective date is shown.
Open-lot rows render explicit Long/Short direction in the table, detail panel, and accessible
row description rather than inferring direction from the positive lot quantity.
The Settings workspace also owns the browser asset-profile governance surface for custom Security
Master assets: it reads approved profiles, drafts profile variants from starter templates, submits
approval/rollback lineage actions through the shared Security Master endpoints, and creates
profile-backed `CustomAsset` records pinned to the approved profile version. React should keep this
as a thin orchestration surface over shared DTOs and API validation; do not add browser-local
scripted validation rules.
Settings also renders the scoped-access assignment console over the shared Auth API. The browser
lists active or revoked assignments, grants principal authority with role, optional profile, scope,
permission names, effective dates, approval-limit metadata, segregation-of-duties rule text,
rationale, and correlation metadata, and revokes assignments with the current assignment version so
optimistic-concurrency and audit evidence remain server-owned.
Settings also exposes the Admin Ops task console over shared maintenance, storage, retention,
cleanup, schedule, and data-package endpoints. React renders returned posture, command results, and
typed package/schedule evidence without inventing local maintenance policy or retention decisions.

Evidence Workbench consumes the shared Evidence Vault request-list and document queue endpoints. The
same intake queue is reachable from the Accounting and Data workspace subnavigation, while the
legacy Reporting evidence route remains a compatible evidence-packet entrypoint. The
browser renders retained documents with classification, source hash, typed channel/source, actor,
tenant/scope, extraction status, reviewer state, linked operational objects, open support-request
count, support-only authority posture, and manifest links, while keeping intake and readiness policy
in shared contracts/endpoints.
When the statement-run service returns nothing, the reconciliation desk derives run rows from the
reconciliation queue. The queue carries break and case counts but no match totals, so derived rows
report match counts as not reported (`—`, with the reason on the row) rather than printing the
placeholder zeros, and the Positions, Cash, and Transactions detail tabs drop their badge and say
the totals were not reported instead of crediting the reconciliation service for them. A
service-reported run that genuinely matched nothing still reads `0`: zero and unknown are different
facts in a reconciliation.
Statement import previews an uploaded file against the fund account and reporting period from the
Commit import form, so the panel cannot parse anything until those fields are complete. That
dependency is stated on the panel: selecting a file with the form still blank names the outstanding
fields instead of silently doing nothing, and the commit control reports the same fields rather than
asking for a preview it is holding back. Picking a connector fills Source institution from that
connector's display name when the field is still blank; fund account and period are never guessed.
Statement import accepts either a bounded file upload or a remote fetch through a fetch-capable
provider connection. The scheduled-fetch tab previews remote activity with the same canonical
column-confidence and per-kind breakdown as file import, then lets operators create, edit, pause,
delete, refresh, or run persisted schedules with an explicit broker/custodian classification without
collecting credentials in the browser. Run-now
results render the shared Evidence Vault and reconciliation routes. File-import commit results also
render Evidence Vault identity, the Evidence Workbench route, reconciliation route, and structured
account-margin, activity-completeness, option-lifecycle, tax-lot, and borrow evidence retained beside
the canonical CSV. `/accounting/margin-control` projects that retained evidence across accounts and
prime brokers, compares provider-authoritative values with a labelled Meridian shadow estimate, and
permits durable end-of-day certification only through the shared permission-checked endpoint.
reconciliation case links directly from the commit response, including status, priority, reason,
and suggested next action. The browser blocks file commit while preview errors remain, so operators
can move from imported custodian/broker source to retained proof and exact casework without
browser-local routing rules or avoidable server rejections.
The request-list queue renders typed close, audit, tax, report-package, and operational-event family
badges beside each frozen support list so operators can distinguish close binder blockers from audit
or report-support package gaps without parsing manifest JSON.
Selecting a retained document opens a read-only review panel with reviewer notes, source metadata,
immutable source-record receipt, extracted fields for human confirmation, human-confirmed field
counts, authority boundary, object links, audit events, support-request context, and manifest access
without introducing browser-owned approval or accounting mutation.
Operators can also retain an uploaded document, local-file path, or imported-file reference from
the selected evidence subject, classify it, record actor and tenant/scope metadata, set extraction
and reviewer state, and attach one linked operational object before the shared vault intake
endpoint copies the payload and computes the source hash. The intake form exposes the v1
document-classification vocabulary, `Pending` extraction posture, and linked fund, account, period,
close, reconciliation, journal, report, instrument, and portfolio object targets; it leaves `Accepted`
review out of first-pass intake because accepted evidence must pass through the shared review
endpoint with human-confirmed fields.
When an operator accepts a retained document from the browser, the shared review endpoint receives
human-confirmed extracted fields plus the immutable source-hash field so accepted evidence is not
represented by a status-only transition; the retained document authority still cannot approve,
post, certify, or release.
The TypeScript contract also mirrors `EvidenceDocumentIntakeChannelDto` and
`EvidenceDocumentIntakeSourceDto` so uploaded, email, SFTP, API, portal-download, local-file, and
imported-file reference intake use the same shared channel/source vocabulary. Browser intake keeps
email, SFTP, API, and portal-download as upload-backed adapter seams in v1: operators retain the
document bytes and typed source URI now, while later connector implementations can fetch from those
channels without changing the retained source-record shape.
The TypeScript vault identity mirror includes the public `manifestSnapshot` so browser close,
report, audit, and tax package views can inspect frozen package documents, support requests, object
links, typed request-list family, typed frozen package family, and content hash without parsing
retained manifest JSON.
Manifest export results surface that typed frozen package family directly, so operators can tell
whether the retained package is a close binder, audit packet, report support package, tax support
package, or operational-event support package before opening the manifest file.
The browser DTO mirror also preserves `documentSnapshots` on Operations Continuity close-package
requests and publications so close binder documents can be frozen by Financial Operations instead
of recomputed in React.
The browser route catalog, endpoint helpers, DTO mirrors, and API client also expose the shared
v0.19 provider-integration runtime for template catalog/detail, OpenAPI import, setup-save,
activation-readiness, dry-runs, activation, monitor, sync-run history, sync planning, due-sync
execution, schema-drift checks, staging review, identity resolution, promotion readiness,
reconciliation handoff, quarantine review, quarantine resolution, and quarantine replay. Browser
screens should consume those shared endpoints when binding setup and review panels instead of
deriving provider runtime state locally.
The Settings Provider Connection Center now loads the shared provider-integration monitor,
sync-run history, sync plan, staging review, identity-resolution preview, promotion readiness,
reconciliation-handoff history, and quarantine review on demand per routed connection. Retained
staging, identity, quarantine, promotion, and handoff evidence stays service-owned while the browser
surfaces the operator review posture beside credential and routing status. The runtime panel can
seed an OpenAPI draft manifest, run due read-only sync capabilities, create reconciliation handoff
evidence for unhanded promotion-ready staging rows, record retained quarantine-record decisions for
review-only, replay-after-mapping, ignore-provider-record, and mark-as-cash-position actions, trigger
the shared quarantine replay endpoint for the currently retained quarantine batch, and show the
backend-computed pending, decisioned, replay-requested, ignored, and cash-position posture counts
after evidence reload.

No-host browser previews must keep fixture data visibly labeled as demo data. The masthead's compact,
non-dismissable provenance strip routes operators through the typed demo evidence path: watchlist,
live quote evidence, trading readiness, and provider setup, while keeping retry-to-live behavior
available. Do not add a second full-width seeded-data warning below the masthead.
The masthead environment badge carries the visible provenance label, so `DataProvenanceBanner`
renders as a 4px top-edge stripe with the same status role and full text for assistive technology
(`labeledInMasthead`). LIVE sessions, unresolved sessions and `unknown` provenance (it carries the
retry) always keep the full-width banner.
The app-shell data-provenance badge combines the server-owned `/api/demo/mode` response with actual
fixture usage. Explicit demo or fixture data always wins over a nominal live posture, and a missing
or malformed mode response fails closed to simulated/unverified rather than being presented as real
provider data.

Refresh-capable browser modules use the shared `useRequestLifecycle` hook for request versioning,
stale response discard, unmount-safe state updates, AbortController handoff, and retry/backoff status
metadata. Keep overview bootstrap, live quotes, backfill preview/run, trading readiness handoffs, and
command-triggered refresh paths on that lifecycle instead of adding ad-hoc revision refs.

Shared workflow targets must land on the same operator lane as WPF. `FundTrialBalance` resolves to
the browser accounting ledger route (`/accounting/ledger`) so Lane B/W3 continuity actions from the
shared workflow registry do not collapse to the accounting root.
Browser workflow target routing accepts the desktop compatibility page tags
`ResearchShell`, `DataOperationsShell`, and `GovernanceShell`, but materializes them as canonical
browser routes under `/strategy`, `/data`, and `/accounting`.
Trading readiness work-item actions consume shared route metadata when it is specific enough to
resolve locally. Execution-control and promotion-review items carrying the shared
`/api/workstation/trading/readiness` route land on `/trading`, while paper-replay items keep the
session replay panel hash target so replay verification remains directly actionable. This is the
browser PaperSession route handoff covered by `DIA-BROWSER-WORKSTATION` and
`DIA-PAPER-SESSION-REPLAY`; keep route changes aligned with those diagram records.
Broker execution reconciliation readiness from the shared Trading payload is rendered as a
broker-order checkpoint in the readiness console and a Trading summary row when present; critical
broker/OMS parity work items route back to the Trading readiness surface instead of being treated as
anonymous inbox rows. The browser also renders shared live-operation readiness as its own Trading
summary row and Operator Readiness Console checkpoint, using `LiveOperationBlockers` instead of
promoting paper-ready posture into live readiness. When the shared payload carries
`LiveOperationRequirements`, Trading summary rows also show each W7 requirement from the
service-owned matrix so browser copy names the exact trusted-data, reconciliation, governance,
rollback, retention, or broker-parity evidence gap. Eligible live promotion evaluations also
project the full W7 checklist instead of the paper baseline alone, including broker execution
reconciliation evidence before the approval request can carry a live-ready checklist. The browser
promotion form keeps retained evidence references as an explicit operator-entered field and
validates live approvals against the checklist tokens instead of fabricating evidence from the
checklist alone. Live evidence references must include retained evidence after each `TOKEN:` prefix,
and the live-override reference must name the active override id before the form can submit.
The Trading order ticket also carries the active normalized `fundAccountId` into order-submit
mutations so the execution-layer live-readiness gate evaluates broker sync and broker/OMS
reconciliation against the same account scope shown in the browser readiness payload.
Close-position confirmations use the same normalized `fundAccountId` for keyed position action
mutations, while non-GUID broker labels are omitted so account scope is not inferred from display
text.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, priority, SLA badge label/tone, age band, root cause, resolution code, last
comment excerpt, comment/evidence counts, related-case counts, required sign-off role/status,
source origin/fingerprint, and decision note so browser recovery posture matches the WPF desktop
Fund Ledger detail panel without reimplementing casework rules.
Accounting reconciliation casework actions now consume the server-owned verified outcome on every
assign, resolve, waive,
supersede, comment, and lifecycle transition. The queue exposes item-level value, quantity, and
cost-basis measures, exact fund/book/period/as-of scope, continuity blockers, immutable evidence and
approval lineage, and conflict-safe replay receipts. Browser success UI is driven only by
`Succeeded` or `CompletedWithWarnings`; `Blocked` and `Failed` retain recovery guidance and do not
optimistically mutate local state. Material waiver or supersession remains unavailable when the
server cannot resolve independent approval evidence.

Accounting reconciliation narratives use canonical Accounting review language while retained
Governance view-model names remain compatibility seams.
Accounting reconciliation statement runs now use the shared statement-run endpoint/client seam for
broker or custodian, account, period, status, validation, match, break, case, and import timing
read models; React components only render these values and do not reimplement matching, tolerance,
validation, or case-state rules.
The Accounting external-GL panel renders the shared accounting-system reconciliation evidence
packages for external import, Meridian ledger support, and GL tie-out posture when the API returns
them, keeping package readiness and required actions service-owned rather than deriving package
state from browser table rows.
The panel also renders provider-by-provider import posture from the shared AccountingSystem provider
catalog, so QuickBooks, Xero, NetSuite, and fixture providers show read-only import capabilities,
credential state, retained mapping-profile coverage, and live-posting-disabled posture before an
operator prepares a guarded export package.
The same panel now reads external GL mapping profiles from the shared AccountingSystem endpoint,
shows certified account and dimension coverage beside the reconciliation, and posts guarded export
package requests with mapping, reconciliation, fund, period, and evidence context. It can also post
the retained export package id, reviewer notes, and evidence to the shared export certification
endpoint. The returned package is retained as a review artifact with generated mapped export lines,
certification, validation state, and a controlled manifest hash; certification evidence must
identify the retained export package id or exact export period before Financial Operations will
certify it. The browser also renders export
safeguards for balanced reconciliation, certified mapping coverage, critical package blockers,
manifest hash/line retention, and disabled live-posting posture while shared policy remains
authoritative. Live external GL posting remains disabled by shared policy and is not inferred in
React. The same panel queries retained guarded export package history for the selected provider,
fund, and ledger book, then renders certification state, evidence count, validation issue count,
period, created timestamp, and disabled-posting posture so operators can review prior export
artifacts without re-entering package ids.
The Accounting screen also carries a stable Investment Accounting Transaction Lab panel view model
so the browser renders the Books Before Broker preview entry point without crashing while endpoint
request wiring remains a follow-on workflow.
The Accounting workspace workflow launch strip is derived from the Accounting view model and shared
route catalog, covering setup, journal entries, ledger review, reconciliation, exception casework,
Security Master readiness, approvals, and retained evidence packaging without browser-local close
state.
The Accounting Configure workstream at `/accounting/configure` renders the shared Accounting Rules
Studio from `AccountingRulesStudioDto`, `PostingRuleDto`, `RuleDryRunResultDto`, and rule-test suite
DTOs: server-computed rule counts, promotion queues, activation readiness, effective dates,
priority, dimensional scope, event predicates, grouped `All`/`Any` predicates, formulas, allocations, generated posting metadata,
retained versions, promotion approvals, dry-run preview results, saved regression cases, and
regression test-case results are browser projections over the shared configuration service rather
than client-local rule logic. Operators can duplicate the selected posting rule into a
promotion-gated draft through the shared posting-rule upsert endpoint; the browser clears carried
approval state, retains browser evidence links, selects the returned draft rule, and lets service
validation/audit own the canonical workspace state. Rule mutation, promotion approval, and
regression-test actions carry the active `ledgerBookId` so book-scoped configuration changes do not
fall back to fund-level rules while the operator is working inside a selected ledger book. Operators
also see setup-readiness rows from shared validation; a missing registered ledger book is surfaced
as the critical `configuration.ledger-book-missing` activation blocker instead of being hidden in
the generic validation list. Retained migration run artifacts render their canonical fund, book,
entity, cost-center, counterparty, and external-GL dimensions so dimensional backfill proof is
visible in the same browser control plane that consumes the shared readiness blocker codes.
Tenant-administration setup in this browser workstream retains structured approval-queue and
dimension-mapping configuration in the shared accounting tenant-administration profile, including
provider id, Meridian/provider dimension rows, and evidence requirements instead of a checkbox-only
readiness claim.
Operators
can apply the selected dry-run source
event as a required predicate through the same shared upsert route, replacing stale event-kind
predicates, clearing stale promotion approval, and requiring fresh promotion before activation.
Operators can apply the selected dry-run amount as a required amount-threshold predicate through the
same shared upsert route, which updates the retained rule definition, clears stale promotion
approval, and requires fresh promotion before activation. Operators can also apply the selected dry-run date as the retained rule effective start,
with stale promotion approval cleared before the updated effective window can activate. Operators
can capture dry-run generated posting lines back onto the selected rule through that shared upsert
path, turning template-derived previews into retained multi-line generated posting definitions that
require fresh promotion. Operators can also apply a dry-run generated posting amount back onto the
matching retained rule formula through the same upsert path, clearing stale promotion approval before
the recalibrated formula can activate. Operators can apply unambiguous generated posting dimensions
onto retained allocation targets when formula-linked generated postings produce deterministic target
scope, again clearing stale promotion approval before activation. Operators can apply unambiguous
fund, entity, instrument, counterparty, cost-center, tax-lot, and external GL dimensions from
generated posting metadata back onto the retained rule scope through the same upsert path, with stale
promotion approval cleared before the scoped rule can activate. Operators can raise the selected
rule priority through the same retained upsert path, clearing stale promotion approval before the
reprioritized rule can activate or be duplicated into a draft. Operators can also archive the selected posting rule through the same
upsert route so obsolete rules leave active matching without deleting retained history or bypassing
audit evidence. Operators can save the selected dry-run preview as a retained regression case through
the shared test-case endpoint, including browser evidence links, the selected rule version, and expected generated posting-line
assertions copied from the dry-run preview, and the refreshed workspace becomes
the source of truth for saved cases. When saved cases exist, the browser lets the shared service
execute the persisted workspace suite instead of rebuilding temporary cases locally. Promotion
approval controls use the shared promotion-approval route so stale rule versions, missing approver
notes, weak evidence, missing saved current-version regression coverage, and failing saved dry-run
assertions are rejected by the service before the retained version snapshot is updated;
the Accounting Rules Studio toolbar updates from the returned workspace rather than fabricating
approval state locally. The selected-rule detail also renders a promotion readiness checklist for
version history, approval evidence, saved regression coverage, latest suite result, generated
posting lines, dimensional scope coverage, and the activation gate. Activation controls surface the
shared readiness blockers for promotion-gated rules, including missing approved promotion evidence,
missing current-version saved regression coverage, and the latest failing rule-test suite. Direct
Rules Studio callbacks for duplicate draft, archive, promotion approval, and activation now return
the same disabled reasons before shared-service mutation when no active rule is selected, promotion
is already approved, or activation blockers remain.
When shared validation reports a missing selected ledger book and the workspace includes a
server-derived setup candidate, the browser Accounting Configure surface can create the ledger book
through the shared `/api/ledger/books` endpoint and then refresh the workspace; React does not infer
fund-structure node ids, accounting basis, or policy ids locally.
The same surface now renders a ledger-book administration catalog from the shared configuration
workspace, showing the selected book, available books, fund/entity scope, basis, currency, policy,
description, and update timestamp before production-readiness review so operators can inspect
which book is being certified without relying on hidden fund-level context. When operators save tenant-administration controls with book
administration enabled, the browser adds a retained tenant-admin evidence reference for the selected
`ledgerBookId` unless the operator already supplied one, keeping the setup editor aligned with the
shared production-readiness gate for multi-ledger administration. The browser tenant-admin setup
editor persists chart administration, Rules Studio test/promotion, close setup, provider mapping,
tenant/company/report-group setup, audit review, bulk import/export safeguards, performance
validation, recovery runbooks, ledger-book administration, posting-rule authoring, approval queues,
dimension mapping, and sandbox validation as independent shared profile controls, and its
implementation-sandbox proof action retains selected-ledger-book implementation-sandbox,
sandbox-validation, fixture-validation, and implementation-fixture evidence while carrying the
current approval-queue and dimension-mapping setup payloads before refreshing readiness; the sandbox
action is disabled until any configured approval-queue or dimension-mapping setup is complete.
The same Configure surface also calls the shared `/api/accounting-system/production-readiness`
assessment for the active fund and ledger-book scope, then renders the service-owned control-plane
posture for ledger books, Rules Studio, posting rules, JE lifecycle, dimensions, external GL,
close/reporting, migration rollout, and tenant administration. The panel displays returned blockers, suggested
actions, evidence counts, retained migration run artifact posture, explicit ledger-book-native
workflow control counts and retained ledger-book-scoped workflow evidence for posting rules,
JE lifecycle, close/reporting, external GL, reconciliation, direct-lending projections, and
strategy ledger reads, dimensional ledger/query/report/export control counts
with retained ledger-book-scoped evidence, certified external-GL mapping coverage, and the disabled live-posting stance without deriving
production-readiness policy in React. Tenant administration uses the shared
`AccountingTenantAdministrationReadinessDto` to render
tenant, company, admin-role, scoped-access, reporting-group, aggregate operator-surface, browser
accounting admin-studio, WPF accounting admin-studio, chart administration, rule-test/promotion
setup, close setup, provider/external-GL mapping setup, tenant/company/report-group setup, audit
review tooling, bulk import/export safeguards, performance validation, disaster-recovery runbooks,
ledger-book administration, posting-rule authoring, approval queues, dimension mapping, implementation sandbox validation,
and retained-evidence controls instead of treating setup readiness as a generic component row, and the
dashboard route catalog exposes the retained tenant-administration profile endpoint. The Configure
surface can load, edit, and save that retained profile with browser accounting admin-studio,
enterprise configuration studio lane coverage, approval queue setup fields for queue id, workflow
kind, required role/count, segregation policy, and retained evidence requirement, and setup evidence
before refreshing production-readiness posture from the shared Accounting System service. The browser
save model blocks configured approval queue or dimension mapping studios until the typed setup
payloads are complete, and its save callback now rejects missing retained tenant-admin evidence
before any shared-service submission, matching the shared store's fail-closed profile invariant. The shared browser
contract also carries structured tenant-admin dimension mapping rows with mapping/provider ids,
Meridian dimensions, provider dimensions, and evidence requirements for parity with retained
Accounting Configure profile data. The Configure surface also
loads, edits, and saves the retained production-certification profile for ledger-book-native
workflow controls, including reconciliation, direct-lending, and strategy-ledger-read
certification, plus dimensional ledger-line, trial-balance, reporting, provenance, and export
controls, preserving tenant/company/fund/book
scope plus retained evidence through the shared Accounting System store rather than request-only
flags. After the operator supplies retained evidence, checked-control saves augment that evidence with
scoped per-control markers for posting candidates, journal lifecycle, close/reporting, external GL,
reconciliation, direct lending, strategy ledger reads, and dimensional query/export coverage so the
shared store can enforce category-specific certification evidence. Production-certification saves also
retain typed workflow, dimensional, and tenant-administration certification artifacts so enterprise
configuration studio controls can feed readiness through executable lane rows instead of broad flags.
The same Configure panel can author retained external-GL provider mapping profiles, including
provider/profile identifiers, account mappings, editable Meridian and provider dimension maps for
fund/book plus customer/vendor/project-style scope, human-origin mapping certification evidence,
and readiness refresh through the shared Accounting System mapping profile endpoint; disabled saves
fail locally when required identifiers, mappings, or retained evidence are missing.
The Configure panel also exposes a chart account setup editor that saves chart nodes through the
shared accounting configuration chart endpoint with the active fund/book scope, parent path,
financial account id, and retained setup evidence, so browser operators can add operational chart
accounts without using raw JSON or direct endpoint tooling; required chart identifiers and account
path/name/type are guarded before endpoint submission.
It also lists retained migration run artifacts from
`/api/accounting-system/migration-run-artifacts`, including run kind, certification status,
fund/book scope, migrated-record and issue counts, and evidence reference counts, so operators can
inspect migration proof retained in the shared Accounting System store rather than re-entering
request-only evidence. The dashboard API layer also exposes the retained guarded external-GL export
package list query so Accounting surfaces can review package history by provider, fund, book,
certification state, tenant, and company before loading a specific manifest. The same
production-readiness panel renders the generated migration rollout
plan for ledger-book scope, historical journal backfill, dimensional backfill, configuration
promotion, and close/reporting evidence migration, including latest retained run, blocking issue
codes, and required actions for each lane.
It also renders the shared production-gap checklist so configurable multi-ledger accounting,
enterprise configuration studio coverage, guarded external GL integration, dimensional ledger and
reporting coverage, and production-control hardening remain service-owned review items in the
browser Accounting Configure surface, including the issue messages supplied by the shared
production-readiness service rather than only stable blocker codes.
After a dry run selects a rule, operators can build a governed journal draft candidate through the
shared posting-rule candidate endpoint. The browser carries the selected event, amount, dimensions,
policy, counterparty, source evidence, tenant/company context, and browser correlation metadata to
the service, then renders the returned generated lines, retained evidence count, candidate issues,
and pending approval-gated posting command without appending ledger entries or bypassing
journal-entry lifecycle controls.
The Accounting Manual Journal Entry workbench also exposes the shared lifecycle-action endpoint for
evidence attachment, approve, reject, post, reverse, rebook, and lock-after-close. Browser evidence
attachment posts the retained draft version, typed attachment metadata, actor, correlation id, and
evidence links to the shared evidence endpoint, then refreshes from the returned draft instead of
fabricating retained evidence locally. Browser lifecycle commands pass the retained draft version
and evidence links, render the returned transition audit rows with transition id, correlation id,
and retained evidence routes, and show generated reversal/rebook drafts as separate entries instead
of mutating posted entries. The workbench also renders a lifecycle checklist for draft version,
validation, evidence, submission, approval, posting, reversal, rebook, close-lock, and transition
audit posture so operators can see which server-owned gate is ready or blocked before acting.
The Accounting close/report package cockpit reads the shared close-management period plan and
accounting report-package history endpoints, showing checklist dependencies, sign-offs,
materiality, close-calendar milestones, period-lock posture, late adjustments, package
certification, investor statement counts, realized gain/loss, NAV, statement-line provenance,
export artifact certification state, restatement state, validation issues, and retained evidence
counts. It renders service-owned close/report readiness rows from the package bundle when present,
and it scopes retained package history by the loaded close plan's fund, period, and ledger book so
book-specific close packages do not blend into fund-level history.
The cockpit now also renders the close plan's service-owned operating coverage rows for close
setup, dependency graph, sign-off matrix, late adjustments, blocker review, and period lock, showing
the backend readiness state, evidence count, blocker count, required action, and blocker issue
labels before the older task/dependency/matrix detail sections.
The cockpit also renders an end-to-end close workflow control sequence for setup retention,
checklist sign-off, late adjustments, blocker review, report package build, certification, export
manifest inspection, and period lock, with each step's action and disabled state derived from the
same shared close plan and selected report package state. Pending late-adjustment review and active
blocker review steps now surface their own review gates in the sequence, while row-level controls
retain the specific approval/rejection or issue-review commands required by the shared endpoints.
Its certification safeguard rows cover close checklist sign-off, period-lock posture,
late-adjustment review, report evidence, export-artifact certification, restatement workflow state,
blocker counts, and retained package evidence; older payloads still fall back to the browser's
display-only safeguard aggregation. Its package-build
command posts workflow, fund, period, package-seed, and evidence context
to the shared accounting report package endpoint, and its certify command posts the selected
retained package id, reviewer notes, and evidence links to the shared certification endpoint. Its
task sign-off administration lets operators select the ready checklist task, retained signer role,
Approved/Rejected decision, and reviewer notes before posting correlation id and evidence links to
the shared close-management endpoint. Its close setup editor lets operators adjust
materiality thresholds, currency, review role, late-adjustment approval requirement, and the primary
checklist task's owner, due date, required approval role/count, evidence requirement, multi-role
sign-off matrix rows, and dependencies
with retained dependency reasons before posting ledger-book evidence and correlation context to the shared close-plan configuration
endpoint, including the loaded configuration timestamp so stale browser setup edits fail closed instead of
overwriting newer retained setup. Dependency reason text can also use keyed predecessor entries such as
`task-pricing: Pricing package must clear` so each retained edge can carry its own audit rationale. The setup editor exposes retained checklist tasks, predecessor candidates, and sign-off
role candidates from required roles, retained sign-offs, task owners, and the materiality reviewer;
the matrix editor accepts retained `role | count | evidence` rows and preserves unedited task
matrices in the shared close-plan configuration request;
it blocks invalid materiality thresholds, malformed currencies, blank materiality review roles,
missing task ids, non-positive approval counts, blank approval roles, and blank evidence before
calling the shared endpoint. Its period-lock action posts the
current workflow version, selected report package, checklist-control approvals, close-package
manifest context, ledger-book-scoped evidence, and human-operator origin to the shared
close-management lock endpoint, rendering returned service blockers without attempting a local
close transition. The late-adjustment request form
posts the journal entry id, amount, currency, reason, controller actor, correlation id, and retained
late-adjustment/materiality evidence links to the shared close-management endpoint, then refreshes
the cockpit from the returned close plan. Late-adjustment approve/reject buttons post the retained
request id, decision, reviewer notes, correlation id, and evidence links to the shared
late-adjustment review endpoint; close transitions, dependency validation, materiality review
validation, certification state mutation, rendered statement artifacts, and restatement execution
remain service-owned.
Late-adjustment rows render retained approval or rejection actor, timestamp, evidence counts, and
materiality-threshold posture from the shared close-management DTO instead of deriving decision state
or approval blockers in the browser.
Close task sign-off rows likewise display retained approval counts, actor, notes, and evidence from
the shared close plan; React does not decide checklist completion locally, and dependency ordering
is enforced by the shared task sign-off endpoint.
The setup editor exposes a retained close-task catalog before the task authoring fields, so
operators select the task whose owner, due date, approval count/role, evidence requirement, and
dependency ids are being edited; unknown task ids are blocked in the browser before the shared
configuration endpoint is called. Dependency authoring also projects known predecessor candidates
from the loaded close plan as selectable rows, keeping dependency graph edits tied to retained
task ids while still submitting the shared close-plan configuration contract, and keyed entries in either the dependency-id text or reason text are preserved as per-edge reasons.
The close cockpit also projects dedicated dependency-graph, sign-off-matrix, and evidence/blocker
review rows from the loaded close plan and selected report package so operators can audit
predecessor coverage, required approval roles, retained evidence, and service-owned validation
blockers without re-deriving close state in React. Active blocker rows can now post a retained
close evidence-review command with workflow, period, issue, target, ledger-book, notes, and
close-review evidence; returned review rows are displayed beside the blocker while the underlying
validation issue remains service-owned.
Close-calendar rows display milestone due dates, owners, dependency counts, sign-off counts,
evidence counts, blockers, and period-lock state returned by the shared close plan instead of
rebuilding calendar posture from checklist rows in React.
The accounting production-certification editor also carries a separate close-plan setup control so
materiality policy, checklist/dependency, and sign-off configuration evidence is retained by
ledger book instead of being implied by generic close/reporting certification.
Accounting report package history rows also display the shared export artifact certification count
from the package bundle, while artifact generation, content hashes, and certification state remain
owned by Financial Operations. Operators can inspect the selected package's retained controlled
export manifest from the close cockpit; the browser displays artifact id, format, file name,
generation time, content hash, route, evidence count, certification state, and disabled external
posting posture while Financial Operations remains responsible for artifact generation and
certification mutation.
An empty holdings table on the Portfolio desk offers the step that actually fills it rather than
only stating that it is empty: a loaded workspace with no holdings routes to statement import, and
an empty paper session routes to the trading desk. The offered step follows the same branch as the
sentence printed beside it, so the two can never disagree, and a workspace that failed to load gets
no button — that is a load failure, not an empty desk, and routing elsewhere would hide it. The
multi-asset coverage presenters live in `portfolio-screen.multi-asset-coverage.ts`; the row and
group types stay owned by the view model and are imported type-only.
`FinancialRecordExplorerShell` is the browser presentation for the shared Financial Record Explorer
DTO. Accounting loads the `ledger` and Accounting-hosted `security-instrument` explorers from
`/api/workstation/financial-record-explorers/{explorerId}`, Portfolio loads `portfolio`, Reporting
renders the shared `report-line-provenance` explorer from the reporting payload, and Data links to
the existing Security Master lane instead of restoring the old static Data workbench. Reporting
renders the server-provided report-line chain for instrument, position or transaction,
reconciliation, journal, report line, evidence, and audit links; React must not rebuild that
lineage locally.
Saved views post back through the shared saved-view endpoint only when an operator supplies a
stable view name for the current filter/search state; browser code does not generate timestamp-only
saved-view labels. Shared explorer links also round-trip explicit `frexFilter` values alongside the
selected saved view, search text, and proof record, so a discussion URL can restore the exact
evidence state even when the filter is not encoded in a durable saved view. Blocked or empty DTOs
keep proof actions disabled with the server-provided reason.
Operations Continuity close-checklist fields mirror the shared workstation DTO, including required
approval counts, expiration dates, and close-readiness blockers, so the browser reads the same
approval gate state enforced by the API and WPF clients. The browser checklist summary is also
derived from those shared task fields: ready, blocked, acknowledged, approval, evidence-pointer,
and next-due counts are display projections only and must not become client-local close state.
Operations Continuity also renders the shared close-calendar read model from
`getOperationsCloseCalendar`, scoped by the selected workflow fund account and period, so due task,
owner, readiness, blocker, checklist, approval, and route posture stay server-owned workflow-control
evidence rather than browser-local calendar state.
Checklist rows also display shared acknowledgement command posture and the expected workflow
version guard beside retained acknowledgement evidence, keeping close-control acknowledgement
available through the operations-continuity command API instead of browser-local checklist state.
When a checklist task is command-ready, the screen posts acknowledgement through the same shared API
with the workflow version guard, browser operator actor, rationale, and correlation id, then
refreshes from the server-owned workflow payload.
Operations Continuity break assignment and escalation rows likewise render owner, due date,
SLA state/due time, materiality, root cause, approval posture, blocked downstream outputs,
escalation, variance, suggested action, retained evidence counts, and the first local retained
evidence route from the shared workflow payload. They also route operators to the Accounting
exception casework lane with workflow and break context while assignment/escalation and resolution
commands stay on the shared operations-continuity API instead of browser-local break state.
The same rows expose guarded assignment and resolution commands: unassigned breaks can be assigned
to the browser operator with the workflow version, escalation, due-date, and rationale carried to
the shared API, while assigned breaks can resolve only when retained evidence links are present.
The browser API client exposes the same shared operations-continuity command spine for workflow
start, broker import/normalize, gate posture refresh, Security Master resolution, ledger
draft/validate/post, reconciliation run, and approval submission so browser surfaces can call
server-owned workflow transitions without adding local Financial Operations rules.
The Operations Continuity screen projects that spine from shared dashboard metrics as read-only
Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close
Support rows with server-owned guard, evidence, and route labels. The Approve Results row also
exposes a guarded approval-submission command when the selected workflow has no open breaks,
report-pack readiness includes a retained report-pack id, and reviewer ownership can be resolved
from the shared workflow payload. The Produce Evidence row exposes a guarded close-package
publication command only when the shared close-readiness payload is ready and the workflow supplies
retained report-pack evidence plus checklist-control approvals for the close request; permission,
transition, and evidence-hash enforcement remain server-owned.
The Operations Continuity screen also renders the shared Financial Operations operational
dashboard. Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence,
and Close Support metric state, retained evidence, route hints, and required actions come from the
workflow detail payload, so the browser dashboard is a display projection rather than a local
workflow engine. The same screen now renders the workflow detail `Reviewed automation` summary as
read-only stage, review-state, allowed-use, prohibited-action, retained-evidence, required-action,
and reviewed-output artifact posture so automation review remains visible without browser-local
automation policy.
The Financial Operations operator queue on that screen is also derived from workflow detail, the
shared close-calendar projection, and the private-capital close cockpit: reconciliation break cases,
non-ready reconciliation lane posture, workflow blockers, close checklist tasks, close-calendar due
items, non-ready private-capital proof lanes, NAV support packages, workflow approvals,
private-capital close approvals, reviewed-automation outputs, workflow evidence-package readiness,
accounting-record evidence categories, close-cockpit evidence-package readiness, and non-ready Receive
Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close Support
command stages stay source-backed while React only groups the active work items for review. If the
close-calendar or private-capital close cockpit projection cannot load, the queue adds a blocked
unavailable item so workflow control fails closed.
The same workflow approval payload also feeds the approval-history table with submission timing,
reviewer/operator attribution, rationale, status, and retained approval evidence routes. Pending
rows expose guarded approve/reject commands only when the selected workflow supplies version,
reviewer, report-pack, and checklist-control evidence required by the shared operations API; the
server still owns permission, transition, audit, and evidence validation.
It also renders the shared evidence-package table from the workflow detail payload: accounting
record evidence, report-pack evidence, close-package manifest, and audit-support package rows show
server-owned readiness, category completeness, retained evidence counts, local routes, and required
actions instead of stitching package state together in React.
The Accounting approvals workstream at `/accounting/approvals` reads the same operations-continuity
workflow list/detail payload and posts approve/reject decisions through the shared approval
endpoints, keeping signer, report-pack, blocker, and audit-trail evidence server-owned.
The bootstrap hook also fetches the shared workflow-summary endpoint and passes the active
`fundAccountId` from route or stored shell scope when that account scope is present, allowing the
Accounting Closeout strip to project source-backed Operations Continuity exceptions, approval
history, close readiness, and evidence package posture instead of overloading profile identifiers.
For Trading, the hook and screen refresh paths pass the active account scope to the shared trading
workspace, trading-readiness, and operator-inbox endpoints only when it is a GUID, preserving
account-scoped brokerage sync and broker-execution reconciliation evidence without sending display
account labels into GUID-bound API parameters.
The app shell consumes the shared Financial Operations `Reviewed automation` evidence badge as an
operator-focus item only when the badge requires review. React may route the operator back to the
source-backed workflow step, but it must not approve, post, publish, release payments, erase
evidence, or invent autonomous automation state outside the shared summary. Browser DTO typings now
carry `OperationsReviewedAutomationSummary` plus optional material-command `actionOrigin` fields,
while the shared endpoints force authenticated human origin before Financial Operations accepts
ledger posting, approval, payment release, report publication, report delivery package creation,
close-package publication, or governed reopen commands.
The close-package panel is likewise a read-only projection of the shared operations-continuity
publication metadata: signer, sign-off rationale, retained manifest route, evidence hash, report
pack id, retained evidence links, and checklist control approvals come from the server workflow
payload rather than browser-local publication state.
The browser API client also exposes `getPrivateCapitalCloseCockpit` for the shared v0.18
private-capital close cockpit endpoint, keeping fund/book/period/entity close lanes server-owned
instead of rebuilding close readiness, delivery, report, or period-lock posture in React.
The Operations Continuity screen renders that private-capital cockpit as a compact read-only lane
and scoped-workflow panel, including shared readiness, blockers, fund-event counts, report-output
delivery, capability labels, server-routed next action state, and a Financial Operations proof-lane
summary for partner capital tie-outs, expense/fee allocation review, NAV support, evidence packages,
and period-lock evidence. It also renders shared approval history from workflow approval decisions
and checklist-control approvals, private-capital evidence package rows for fund-event accounting,
partner capital tie-outs, NAV support, and close approval/audit evidence, plus NAV support package
rows for positions, cash, pricing, shadow NAV, and retained evidence links so approval,
evidence-package, and NAV-support evidence are visible without browser-local timeline or
report-output reconstruction. Approval-history and evidence-package rows preserve the first local
retained evidence route alongside the workflow or package route so audit evidence remains directly
reachable from the close cockpit tables.
The period lock and reopen evidence panel stays read-only as well: locked/open/reopened posture,
close audit hash, close package hash, reopen incident correlation, rationale, and retained reopen
evidence are derived from the shared workflow status and timeline. The panel can submit a governed
reopen packet only when the selected workflow is closed and an operator enters incident id,
approval reference, justification, and impact summary; the shared endpoint still owns actor,
governed-admin permission, close-lock, and transition enforcement.
The Operations Continuity detail view also renders the shared accounting-record summary from the
workflow payload. Retained source records, normalized activity, reconciliation case history, ledger
evidence, approvals, and report-pack lineage are displayed as server-owned evidence categories, not
browser-local audit-readiness rules. Each row also displays the contract-owned required evidence
labels, including document attachments, export manifests, and restatement lineage for report-pack
evidence. Browser evidence clients also carry the shared vault lookup and export linkage contract,
including `accountingRecordId`, so retained accounting-record manifests can be rediscovered through
the same API shape used by WPF and host endpoints.
The same detail view renders contract-owned reconciliation lane coverage for cash, position, trade,
income, MBS factor, bank, and GL support. Lane readiness, break counts, retained evidence counts,
first local retained evidence routes, lane route hints, and required actions come from the
Operations Continuity payload rather than browser-local reconciliation heuristics.
No-host browser previews include the same accounting-record evidence subject, packet, validation,
manifest export, and vault-search fixture path so operators can inspect the evidence workbench demo
without a live Meridian API host.
Shared workflow targets that carry `EvidenceWorkbench:accounting-record/{recordId}` resolve to the
browser evidence workbench with the accounting-record subject query intact, matching the WPF
desktop route-parity rule while keeping the browser page tag catalog explicit. Evidence Workbench
packet actions use the same subject-aware route while displaying the operator target as
`Evidence Workbench` instead of leaking parameterized page-tag syntax. Saved workflow presets use
the same route helper, so pinned accounting-record evidence commands preserve subject and operating
scope when launched from the command palette.
Evidence Workbench also renders the shared v0.18 Operational Evidence Graph proof-chain coverage
for Source, Normalization, Reconciliation, Ledger, Capital accounts, Close, Reporting, Delivery,
and Audit layers before the existing lineage and node inspectors. Keep this panel driven by the
packet `proofChain` contract rather than browser-local node classification. Manifest export results
also render the shared vault request-list groups and support request rows beside retained artifacts,
including target kind, target id, highest severity, evidence kinds, missing or blocked evidence,
source system, work item, and blocked output metadata from the server contract instead of rebuilding
audit, tax, close, or report-package request lists in React. The workbench also calls the shared
`/api/workstation/evidence/vault/request-lists` endpoint to show open frozen request lists for the
selected subject, or the latest open vault request lists when no subject is selected. Retained
artifact rows also surface server-provided capture channel/source metadata and extracted-field
validation summaries so the browser does not infer document intake, confidence, or reviewer state
locally. Evidence nodes may also carry optional metadata for typed proof identifiers such as
report-pack delivery package ids; React treats that metadata as server-owned context and does not
parse node summaries to rebuild vault linkage.
Operator readiness console API source identifiers use the canonical workspace roots
`strategy`, `data`, `accounting`, and `reporting`; legacy payload type names and retained
compatibility routes must not reintroduce visible `Research`, `Data Operations`, or `Governance`
root keys. Demo fixtures, trust-gate owner lists, affected-workflow labels, and Security Master
evidence packet distribution labels follow the same canonical root naming.
The app-shell trading continuity title uses `Trading Controls` so cross-workspace recovery copy
does not reintroduce `Governance` as a visible workspace label.
Reporting workspace status rows consume shared template metadata and recent run projections for investor statements, SEC filing packets, and shadow NAV packs; React renders approval status, retry attempts, audit actions, and lineage completeness rather than reimplementing report orchestration rules.
The Reporting workspace also renders the shared `AccessAudit` summary from `WorkstationReportingPayload`,
showing matched user/group/company scopes plus aggregate visible/hidden counts for templates, report
packs, schedules, deliveries, and structured exports. React displays the service-owned denial
reasons without probing or naming hidden report objects.
On the daily Reporting landing, React renders `starterKits` and `starterKitState` from the shared
Reporting payload as the "Set up your reporting desk" chooser. Selecting a kit posts to the shared
starter-kit provisioning endpoint, then shows the server-returned enabled template ids, layout id,
default period, and draft schedule ids; the browser does not locally decide which templates or
schedules belong to an archetype.
Report-pack delivery history rows link delivered packages to the shared `report-pack-delivery`
Evidence Workbench subject using the backend `reportId:attemptId` identity, so React does not build
delivery evidence packets or audit graph state locally. Publication review and delivery package
panels render retained line provenance with the server-provided Financial Record Explorer hrefs
rather than constructing ledger, portfolio, or Security & Instrument routes in React. The delivery
history also renders backend-owned access, channel, and download summaries for retained legacy
email-link, secure-portal, evidence-vault, and internal-route package labels instead of deriving
recipient-facing copy or transport capability from a label or URL shape. When the shared package
includes an access-expiry timestamp, React displays it
beside the token-gated delivery link so operators can see when email-link or portal access closes.
Schedule delivery-plan cards render the latest retained delivery access expiry, access/channel summary, and entitlement
scope from the shared plan payload beside artifact integrity, retained download summary, notification proof, report-writer
dataset/grid summaries, branding, and retained access links, so operators can verify private or restricted scheduled packages without
opening the package manifest first.
The Reporting workspace also renders shared `portfolioCuts` rows for fund, strategy, and tag
reporting views. The browser shows exposure, cash, P&L, shadow-NAV, variance, source-count, and
version stamp from the backend payload instead of recomputing those cuts in React.
Structured export cards render regulatory, data-warehouse, and investment-decision rows from the
shared `structuredExports` payload, including retained paths, retained manifest paths, schema
version, source counts, integrity summaries, raw SHA-256 hashes, data-dictionary links, evidence
links, and classification tags before exposing JSON/CSV/XLSX downloads. Operator-facing XLS or
Excel requests use the shared route's `format=xls`/`format=excel` aliases and receive the same
canonical `.xlsx` workbook artifact. The shared download routes stamp JSON, CSV, and XLSX responses
with export id, generated-at, actor, company, report group, version, and integrity headers while
keeping browser tables read-only over backend-owned export state.
Cross-fund consolidation cards follow the same rule: fund/entity counts, gross/net exposure, cash,
P&L, shadow-NAV, variance, source counts, readiness, and drill-through routes are rendered from
`crossFundConsolidations` without browser-local roll-up math.
Report-pack delivery cards render branding evidence from the shared package payload, while the
canonical artifact-vault downloads keep styled artifact generation in shared services. PDF output
applies the selected theme colors, firm, logo, footer, and disclaimer server-side, and XLSX packages
retain a Branding worksheet, so the browser exposes retained branding evidence without recreating
presentation rules in React. Email-link and secure-portal recipients use server-issued, scoped
access-grant exchange links with a single opaque fragment token. The browser rejects cross-origin,
query-credential, and legacy raw-package routes and does not turn retained manifest paths into
recipient downloads.
It also renders shared `livePortfolioViews` rows for tick-linked portfolio reporting. React shows
the backend-owned live/source-backed/stale/blocked state, gross/net exposure, cash, pending
settlement, P&L, shadow NAV, liquidity and telemetry copy, source/cut freshness stamps, and links to
the portfolio-summary and cash-ladder routes rather than deriving live freshness locally. The rows
also render contract-owned market tick timestamp, tick age, safe tick sequence, provider label,
tick freshness summary, freshness-policy thresholds, policy reason, and live-link flag so operators
can distinguish true live-linked portfolio telemetry from retained source-backed snapshots. The
Reporting route participates in the same portfolio refresh lane as Portfolio so the server-owned
`livePortfolioViews` payload is refreshed with the source portfolio evidence while preserving
backend freshness classification. The Reporting live-views panel exposes that shared refresh action
directly and also schedules a bounded 60-second auto-refresh only when at least one live view is
`LiveLinked` or marked with `isMarketTickLinked`, so tick-linked reporting views follow the shared
portfolio route without introducing browser-local market-data state. It shows success or failure
feedback from the portfolio refresh lane instead of asking operators to leave Reporting.
`LiveLinked` is rendered only when the shared backend marks the source snapshot inside its freshness
window; otherwise the browser preserves the emitted `SourceBacked`, `Stale`, or `Blocked` state and
renders any contract-owned readiness blockers instead of inventing browser-local live-data copy.
It also renders shared `pnlSlices` rows for daily, weekly, monthly, and yearly P&L. React shows
date windows, realized/unrealized/current/prior/change values, source-backed or blocked posture,
readiness text, version stamps, and backend routes from the payload instead of fabricating browser
period bridges.
The Reporting workspace reuses the shared private-capital activity projection and readiness state
already carried by the manual journal workbench. React projects fund-event ledger records,
capital-account subledger references, ledger impacts, retained evidence categories, approval state,
and report-output posture into a read-only reporting panel, including the shared distinction between
published output and report-ready output when report-specific evidence is missing. It must not
invent browser-local fund-event eligibility, capital-account roll-forward, ledger-impact, or
report-ready rules.
It also renders shared `analyticsRows` rows for Top-N winners, Top-N laggards, and contribution
breakdowns. React shows security/strategy/asset-class scope, rank, P&L, contribution percent,
heat-map intensity, source counts, readiness text, version stamps, and backend routes from the
payload instead of recalculating portfolio contribution analytics in the browser.
It also renders shared `crossFundConsolidations` rows for company, fund, and legal-entity rollups.
React shows source counts, exposure, cash, P&L, readiness, version stamps, and backend routes from
the payload instead of aggregating multi-fund state in the browser.
It also renders shared `structuredExports` rows for regulatory, warehouse, and investment-decision
outputs. The browser displays readiness, format, row/field/source counts, schema version, retained
path, retained manifest path, row-lineage count, SHA-256 integrity summary, raw SHA-256 hash,
dataset id, as-of timestamp, version stamp, exact backend API route, and direct backend links from
the contract. Each ready export exposes JSON, CSV, and XLSX download actions by normalizing the
shared retained route's `format` query instead of building browser-local export payloads. Blocked
exports stay visible with validation copy, but the browser renders contract-owned readiness blockers
and disabled download controls without anchor `href` values until the backend marks the descriptor
ready.
It does this instead of deriving export inventory from report-profile labels.
The Reporting workspace also renders shared `brandingThemes` rows with firm identity, built-in or
custom posture, color swatches, logo URI, footer text, and disclaimer copy. Operators can stage a
custom branding override for schedule or run configuration, but the browser does not call the
retired pack preview or generation endpoints. Preview, certified artifact production, lifecycle
creation, and release now proceed through the canonical governed-run parameter and run-detail
workflows, where readiness and retained evidence remain server-owned.
It also renders shared `scheduleDeliveryPlans` rows for scheduled report packs, including recipient,
channel, delivery mode, PDF/XLS/CSV formats, readiness, retained package links, access-expiry
timestamps, retained download summaries, latest artifact counts, checksum integrity summaries,
schedule/package branding theme, and version stamps from the backend payload. Each ready
delivery-plan row can also run its owning schedule through the shared schedule-run endpoint and
reports the returned run id, delivery count, recipient-specific delivery count, and target
delivery mode in the common schedule status lane.
Those rows render typed drilldown links and retained next-action references from the shared payload.
Browser-safe evidence routes remain inspectable, while legacy pack actions are presented only as
historical context and route operators to the canonical governed-run detail. React does not post
caller-supplied signers, hashes, manifest ids, retention paths, approvals, publication, archive,
restatement, or delivery commands to retired pack lifecycle endpoints.
The run cards also surface exact audit metadata from the shared run projection, including run id,
template id, as-of date, trigger, status, attempt count, section count, linked-lineage count, and
retained artifact names, so version-control review does not depend on parsing a prose lineage
summary.
The report-run parameter workspace mirrors the full optional `LedgerDimensionSet` through a JSON
editor. React validates supported scalar fields, UUID-shaped book/instrument/position identifiers,
fund and ledger-book consistency, and the `externalGlDimensions` string map before calling the
authoritative readiness endpoint. The run command then posts the server-returned normalized
parameters, including non-empty dimensions, so readiness and generation operate on the same exact
projection. A code-only ledger selection may omit the dimension `bookId` for server resolution.
Governed run responses consumed by React require canonical `normalizedParameters` and
version-bound `actionAvailability` entries with `expectedVersion`. Any retained legacy parameter
or action aliases are adapted only at the reporting-governance API boundary; screen components do
not infer permissions or versions from those aliases. The same run detail requires the immutable
access snapshot as `allowOwnerAccess` plus typed `User`, `Group`, or `Company` principals. React
renders those values as policy evidence only; flattened principal ids are rejected at the API
boundary rather than assigned a client-inferred principal kind or used to infer authorization.
The Reporting workspace also renders operator-managed schedule rows from the shared schedule
payload and wires schedule save/upsert, run-now, pause, resume, disable, and delete (with an
inline confirm step) controls through the shared schedule endpoints. Cadence presets cover daily,
weekday, weekly, twice-monthly, first and last day of month, quarterly, and annual runs, and a
time zone select sends the schedule's IANA `timeZoneId` (default `UTC`). Due schedules are leased and executed only by the hosted reporting worker; the
browser has no public batch `run-due` control or API helper. Schedule drafts default from the current schedule, approved template, and
distribution payload before posting back to the server. Retained delivery attempts render from the
shared delivery-history payload, so browser Reporting shows actual
recipient delivery state and retry history instead of static status chips. The delivery panel is
read-only: durable dispatch state and provider failure receipts are produced by the server-owned
distribution worker, not a client-recorded failure mutation. Delivered attempts retain package
mode, requested formats, manifest and publication evidence, canonical opaque-fragment access-grant
links, notification proof, artifact integrity, and governed run ids. Runs with retained ids link to
the governed run detail for release and distribution controls. Query-token package builders and
the retired legacy package/delivery API helpers are not exposed by the browser. When a schedule targets a custom report-writer
template that has not produced a published report-pack workflow record, those package rows still
render because the backend now falls back to the generated reporting run and includes its retained
manifest/report-writer artifact provenance on the delivery package; the browser renders the
reporting run, template, schedule, report-writer dataset source, source-artifact provenance, and
generated-run evidence-packet package contents, support evidence IDs, delivery evidence links,
access entitlement, recipients, approval chain, request history, amendments/restatements, audit
references, and blocked downstream outputs beside the package links.
When the package carries generated report-writer grid metadata, the delivery panel also shows the
grid title, kind, dimension/metric/formula counts, and validation summary counts from the shared
package manifest instead of parsing source-artifact strings. If the package also carries rendered report-writer grids, the same
panel shows rendered row/column counts, retained data-dictionary fields, generated-field counts, and
backend validation checks so operators can distinguish descriptor-only packages from source-backed
pivot, Top-N, contribution, and formula output delivered by the backend.
Operators can also save or update schedule records
from Reporting by choosing a template, cron, as-of date, due timestamp, recipient distribution,
delivery mode, PDF/XLSX/CSV formats, and an explicit recipient principal id plus User/Group/Company
kind, then staging multiple delivery targets before save so a single governed schedule can retain
separate typed recipient targets. The browser blocks staging or saving a delivery target until both
recipient fields are present; the server validates the target against immutable access and the
recipient directory, then binds the ordered declarations under `deliveryTargetsSnapshotHash`.
Email Link uses the configured
`http-relay`; retained Evidence Vault and Internal Route mode labels use the local `secure-portal`
path and do not imply separate transport adapters. The browser renders the caller-specific server
transport catalog as the availability authority. It posts the shared
`ReportingScheduleUpsertRequestDto` instead of maintaining local schedule rules, including a
server-owned `datasetSourceId` and the current custom branding override. It never posts governed
dataset rows; the server resolves certified report-writer input. Recurring generated-run packages
therefore retain the selected firm styling without accepting client data as accounting evidence.
Schedule cards also show configured delivery targets
and the run-now status reports returned delivery counts or warnings from
`ReportingScheduleRunResultDto`, keeping generated report runs distinct from packaged
client/internal distributions. The backend applies the same governed template access policy to
schedule saves and manual schedule runs as it does to ad-hoc report runs, so browser controls cannot
schedule or execute user-locked custom templates outside the authorized owner, group, or company.
Schedule records also mirror `releaseDeliveryHandoffs` and `accessPolicySnapshotHash`. The browser
uses those server-owned fields to show whether post-generation delivery is blocked, awaiting
governance release, or enqueued, plus a compact token-free handoff history of durable identifiers,
formats, states, and timestamps. Package creation and bearer-grant issuance remain server-owned and
occur only after release; React does not enqueue a handoff or render retained destination, subject,
or body fields from this schedule history.
The Reporting payload also omits schedule rows, schedule-delivery-plan rows, and delivery attempts
whose template or report-pack workflow is not visible to the current user, so browser state cannot
reveal locked recipients, cadence, package links, or delivery status for another user or group.
Approved accessible report template rows, including governed custom report-writer templates, expose
an on-demand `Run report` command that posts to the shared
`/api/fund-structure/reporting/runs` endpoint and reports the generated ad-hoc run id back to the
operator. Draft, in-review, rejected, superseded, or inaccessible template rows stay disabled for
this command so browser Reporting cannot bypass shared template approval and access policy gates.
When the backend generates an approved custom no-code template, retained
`report-writer://.../grids/{gridId}` artifacts flow back through recent-run rows so browser
Reporting can show that pivot, Top-N, contribution, and custom-formula grids were part of the
actual run evidence, not just a preview-only authoring state. Recent-run cards and delivery
package metadata expose JSON, CSV, PDF, XLS, and XLSX links for each retained grid through the shared
`reportingRunReportWriterGridEndpoint` helper, keeping grid download URLs contract-derived. The
browser XLS link uses the shared `format=xls` compatibility alias and receives the canonical
workbook artifact from the backend, while the browser PDF link uses the shared `format=pdf` route
for allocator-ready retained-grid previews.
The Reporting workspace also exposes `/reporting/operations-record` as the polished W1-W5 release
path. It stays under the canonical Reporting root and projects the loaded Data provider posture,
Operations Continuity accounting-record evidence, close-package state, and report-pack publication
metadata into one fail-closed demo path from source data to accounting record to report pack.
Report-pack distribution UI consumes shared `reportPackDistributions` recipient records, not
static target strings, so the browser surface shows recipient, channel, shared delivery state,
owner, pending item count, due/last-sent posture, route, and pending summary for each governed
package delivery lane.
The Reporting report-pack task also renders publication metadata from the shared backend workflow
record, including signed-off-by, evidence hash, retained manifest path, publication time, and
retained evidence-link count instead of using browser-only publication aliases. Publication evidence
links render as their own retained list, so sign-off manifests and approval packets remain
inspectable even when they are not attached to a specific report line.
The Reporting template panel also renders the shared template authoring lifecycle: built-in versus
custom source, draft/in-review/approved state, latest-approved posture, approval summary, and the
server-owned authoring route for drafting or reviewing a version. No-host fixtures include both an
approved built-in template and an in-review custom revision so the browser surface demonstrates the
version-approval workflow without adding browser-local lifecycle rules. Custom draft/rejected rows
post submit-for-review decisions to
`/api/fund-structure/reporting/templates/{templateName}/versions/{version}/submit`, and in-review
rows post approval or rejection decisions to the matching `/approve` and `/reject` shared
In Report Builder mode a `New custom report` control adds a blank, unsaved report-writer grid whose
field palette comes from the first retained dataset source (or the preview fixture fields). Saving it
posts a new `CustomReport` template draft with no base version; its preview renders the inline grid
against an approved built-in template because the render endpoint requires an approved template.
governance endpoints. When the shared payload marks a template with report-writer grids, the browser
summarizes those grid counts from `reportWriterGrids` and renders a no-code grid designer with
source fields, row/column zones, metrics, formulas, grid-type and Top-N controls, sort posture, and operator-authored
custom formula name/label/expression fields plus filter controls for field/operator/value
slicing. Every saved filter is loaded into the draft as its own row, operators can add or remove
rows, and all complete rows are sent on preview and save (the engine keeps a row only when it
matches every filter). The access-policy principal ID field and the
schedule recipient principal ID field use `PrincipalIdInput`, which suggests role and role-profile
names for `Group` principals and usernames for `User` principals (when the caller may list
accounts) on first focus, while still accepting typed IDs. Source fields come from the shared report-writer field catalog when present, so the browser
palette can include source-backed portfolio, analytics, consolidation, and generated contribution
fields even when a saved grid has not already placed those fields. The designer also renders a live layout summary from the current draft zones so operators
can confirm dimension, metric, formula, filter, and Top-N posture before preview or save. Operators can save the current grid layout as a
governed template draft through
`/api/fund-structure/reporting/templates/drafts`, including company-wide, restricted group/company,
or user-locked access policy metadata. Placed row, column, metric, and formula tokens can be
reordered, moved between zones, or removed individually before preview or save, so operators can
correct a draft without resetting the whole grid. The designer renders the computed draft policy beside the
controls and locks private drafts to a user owner before save. The designer Preview action posts the current unsaved layout
plus an explicit preview dataset profile and bounded sample rows to
`/api/fund-structure/reporting/templates/render`, then renders the returned grid rows, columns,
column roles, data-dictionary fields, backend validation checks, labelled warnings, and audit trace
before publication. Operators can switch previews across combined retained reporting rows, dedicated
portfolio-cut rows, Top-N/contribution analytics rows, cross-fund consolidation rows, and the
certified operational data-mart source from `reportWriterDatasetSources`, representative
portfolio-position, ledger-fact, cash-ladder, and pasted custom JSON/CSV row shapes while the
shared renderer still owns pivot, Top-N, contribution, filter, and formula output. Each
source-backed option posts the selected payload-owned rows to the render endpoint; the browser also
renders the source-owned field catalog with role and data-type metadata before preview. Formula
preview row generation recognizes brace references, bare identifiers, `total(...)`, and shared
formula functions such as `abs(...)`, `min(...)`, `max(...)`, `safeDivide(...)`, `percent(...)`,
`basisPoints(...)`, `round(...)`, and `if(...)`, so local sample rows include the same formula source fields
the shared renderer will evaluate. The
certified operational data-mart option is server-projected with row-lineage keys, lineage manifest
pointers, evidence-index links, source run ids, validation/reconciliation posture, certification
state, and permitted consumers; the browser should display those payload fields instead of deriving
mart metadata locally. The browser uses bounded fixture rows only for the explicit fixture preview
profiles. When an operator changes a draft to a
Contribution grid, the browser preview request prefers the P&L metric as the contribution base,
sorts by `contributionAbsPercent`, and omits generated contribution fields from sample rows so the
shared renderer remains the source of truth for signed and absolute contribution percentages. That trace displays input/output and
filtered-input row counts, source fields, metric source mappings, formula expressions/dependencies,
and filter lineage from the shared renderer. Grid calculation, formula evaluation, filter application,
approval, and retained template truth remain server/shared-service owned.
Approved report-writer template runs and schedules can also choose one of those retained dataset
sources; the browser sends `datasetSourceId` for on-demand and scheduled report-writer automation
instead of embedding raw source rows in every request. Recent-run cards render the resolved source
label and row count returned by the shared run projection, so operators can see which retained
dataset powered the generated grids without parsing artifact names.
Template cards also show a shared access-governance panel with company-wide, user/group, or
user-locked mode, resolved scope, runnable versus blocked posture, and the backend access summary;
inaccessible template rows stay disabled instead of allowing browser-local report runs. Template
cards also render shared audit and version-control metadata: based-on template version,
latest-approved posture, retained audit-event count and last transition, validation issue count,
reviewer, decision rationale, and approval reference all come from the Reporting payload. Recent
generic Reporting run cards use the shared audit drilldown route
`/api/fund-structure/reporting/runs/{runId}/audit` for browser-navigable audit links, so operators
can inspect retained actor/timestamp/action/notes rows directly from the run card.
Portfolio run drill-ins consume the shared attribution, equity-curve, cash-flow, and fill payloads
and project browser view state for run comparison, realized/unrealized P&L bridge rows, and recent
trade evidence. Keep those projections in the Portfolio view model so the React screen renders
shared run evidence instead of recalculating attribution, drawdown, or fill semantics.

Portfolio now includes `/portfolio/family-office`, a family-office route that keeps route metadata,
summary labels, empty states, disabled reasons, and ownership graph/table accessibility labels in the
Family Office view-model seam. The screen view model now derives summary panels and ownership graph
rows from a family-office entity structure shaped like the shared `FamilyOfficeOverviewDto`/
`FamilyEntityDto` contracts, so React renders entity, asset, commitment, reconciliation, and stale
valuation projections instead of carrying a separate graph fixture. The route is **deliberately
withheld from discovery**: it is listed in `UNWIRED_WORKSTATION_ROUTES`, so neither the workspace
navigation nor the command palette offers it while the screen still renders a permanent
not-connected state. It stays mounted, so existing deep links and bookmarks resolve rather than
404. Restore it to both seams — which still read their labels from the route-catalog/view-model
seams, so discovery labels remain centralized — when the family-office endpoints are wired.
Strategy workspace navigation uses canonical Strategy labels for subroutes, including the retained
`/strategy/lab` route, so browser discovery does not expose `Research` as a visible root or
lane name while compatibility routes continue to resolve.
Strategy run-library live-region announcements and command failure messages also use canonical
Strategy wording while retained `Research*` DTO and component names remain compatibility seams.
Strategy Builder promotion-review warnings use risk/control wording in the browser view model,
matching the shared strategy-service validation copy while retained cell kinds remain compatibility
inputs.
Quant Lab treats runtime-parameter extraction as a launch prerequisite for the exact editor source.
While extraction is pending or unavailable, Run stays disabled; a failed refresh may retain the
last usable descriptors and overrides for reference, but those values cannot be submitted for the
new source. Successful refresh replaces removed descriptors and preserves only matching overrides.
This fail-closed behavior and deterministic replay hardening support
[`PRD-106`](../../../docs/product/implementation-todo-list.md) without changing the bounded
`W6-BTSTUDIO-001` Covered Call evidence-loop completion claim.
The host-composed W6 browser path is the Covered Call form in
`covered-call-screen.view-model.ts`. Before calling the Covered Call API it requires operator
acceptance text as a future review requirement and at least one bounded, strict
`evidence://evidence-vault/{vaultId}` reference. The server resolves the retained manifest inside
the authenticated tenant/company scope before queueing and records the pre-execution entry through
the shared strategy-run repository. Covered Call results deep-link to that exact strategy run and
Vault artifact. The browser treats `PersistenceDegraded` as a terminal run phase, stops status
polling, and tells the operator that no Completed, Failed, or Cancelled lifecycle outcome is
authoritative when a durable lifecycle append fails.

The Strategy screen sends Paper promotion through the governed promotion endpoint. Its four
read-only acceptance checks are ready only when the server projects a durable operator/audit
decision, keyed evidence matching the source run, and the exact same-scope Paper child lineage;
metric eligibility, acknowledgement, or a caller-created paper session cannot satisfy them. The
corresponding UI contract proof lives in `covered-call-screen.view-model.test.ts`,
`covered-call-screen.test.tsx`, `strategy-screen.view-model.test.ts`, and
`strategy-screen.test.tsx`;
Strategy Designer and the uncomposed Backtesting Studio orchestrator are not evidence for this
bounded browser path.
The browser `DataScreen` owns the canonical Data workspace module under `src/screens/data-screen*`.
Retained `DataOperations*` DTO, endpoint, and fixture names are compatibility seams only. Data
workspace navigation and command-palette discovery surface `/data/providers` as the canonical
provider catalog and onboarding lane. `/data/operations` is the Ingestion Operations Center for
durable job state, checkpoints, retries, failures, transitions, and Evidence Vault receipts;
`/data/backfills` is a compatibility redirect. `/data/assurance` combines storage health, quality,
canonicalization parity, capacity, and guarded maintenance. Maintenance stays shared-service owned:
React can request a short-lived preview and submit rationale plus exact typed confirmation, but it
cannot choose arbitrary paths or bypass candidate fingerprint revalidation.
The same screen renders the shared data-upload intake panel from the `WorkstationDataPayload`
template catalog. React builds CSV downloads and submits selected files to the shared preview
endpoint only; validation, retained source evidence, and any downstream reconciliation or approval
handoff remain server/shared-service owned.
The provider setup dialog includes Plaid with client-id/secret labels and bank account, identity,
and investment evidence capabilities. Treat Plaid as a server-owned account-evidence connector from
the browser: React submits credentials through the shared provider setup API and links operators
back to `/data/providers` rather than creating browser-local bank-link or market-data routing state.
The Settings Provider Connection Center also captures QuickBooks Online client id, client secret,
refresh token, company realm id, and optional company name through the same shared provider setup
API. React never handles access-token exchange directly; the shared provider service uses that
local config to mark the selected QuickBooks company ready for read-only GL evidence import. Its
inline credential editor renders provider fields and environment choices from shared
provider-connection metadata rather than hard-coded browser provider forms.
Provider setup also projects the design-document Data & Integration flow from its view model:
`Connect Source`, `Acquire Data`, `Validate Data`, `Normalize Data`, `Store Data`, and `Publish Data`.
React renders that flow as status copy only; credential validation, routing, storage, and publication
state remain server/shared-service responsibilities.
The Data provider cockpit consumes the shared `/api/providers/readiness` model as its primary
command-center source, then enriches rows with existing provider connection, routing, trust, and
workspace evidence. React renders provider health, broken credential state, degradation/fallback
evidence, credential field metadata, allowed environments, and the next recovery action from that
shared model instead of recalculating provider readiness locally.
The app shell workflow-continuity view model, shown by `WorkflowContextDisclosure`, also projects
the design-document primary operator workflow as a browser-wide strip: `Import`, `Validate`,
`Reconcile`, `Investigate`, `Approve`, and `Report`. Route-specific trails such as Market Data To
Paper, Research To Paper, or Accounting Closeout remain intact, while the primary strip anchors
every workspace to the financial-operations flow.
Accounting Closeout keeps the design-document Financial Operations lane in the app-shell workflow
context with Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence,
and Close Support while the browser `AccountingScreen` focuses on the owned ledger, reconciliation,
approvals, exceptions, security coverage, and reporting evidence panels. Retained `Governance*` view-model, DTO,
endpoint, and test fixture names are compatibility seams only; new browser component routing should
use Accounting naming.
The Accounting ledger workstream includes a GL account inquiry surface: operators can filter
trial-balance ledger rows by General Ledger account text, select a narrowed account row, inspect
attached ledger-line support when the shared payload includes journal references, and open linked
review packets, source events, journal references, and approval evidence without moving posting
logic into React. Missing journal, source-event, or approval references remain explicit empty states
instead of browser-derived support rows.
Trial-balance rows also render and search retained dimensional scope when the shared payload carries
`LedgerDimensionSetDto` data, including fund, entity, sleeve, strategy, investor, capital account,
instrument, tax lot, cost center, counterparty, and external GL dimensions; dimensionless legacy
rows remain explicit rather than being inferred from account names.
The browser type mirror for retained ledger journal rows carries the same dimensional fields, and
the Accounting view-model exposes a reusable journal-evidence dimension projection for rows returned
by `/api/workstation/runs/{runId}/ledger/journal` so journal review surfaces do not discard scoped
fund/entity/sleeve/cost-center or external-GL evidence.
The browser API helpers for run ledger trial-balance and retained journal reads also accept the
canonical ledger dimension filter set plus external GL dimension keys, so Accounting workstreams can
request server-scoped fund, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot,
cost-center, counterparty, and external-GL results instead of relying on client-only filtering.
The same `/accounting/ledger` route is the first browser implementation of the shared Financial
Record Explorer pattern from the design document. It wraps the existing shared trial-balance,
ledger-line, reconciliation, evidence-packet, audit-packet, and report-usage read models with
explorer scope, saved-view labels, filter chips, summary signals, and proof drill-through actions
without creating a separate browser ledger state or adding new root navigation.
That shared explorer shell also wraps `/portfolio` and `/accounting/security-master`: Portfolio
anchors open holdings, selected run evidence, brokerage posture, and coverage proof to the existing
Portfolio view model, while Security Master anchors instrument search, identity evidence, conflicts,
schedules, lots, and trading controls to the Accounting-owned Security Master view model.

The Accounting Security Master workstream also renders the shared Instrument Passport provider-confidence evidence from /api/workstation/security-master/securities/{securityId}/passport, keeping provider mapping confidence, pricing posture, trust summary, and downstream usage endpoint-owned. The same passport now renders the endpoint-owned operations workbench panels for identity, provider evidence, terms, readiness, and handoff so browser code does not calculate valuation, ledger, reconciliation, close, or report readiness locally.
The Accounting journal-entry workstream at `/accounting/journal-entries` is a thin browser surface
over the shared manual journal entry workbench endpoints. React renders draft headers, GL account
selection, selected-line Security Master search/picker results, line validation badges, typed source
evidence attachments, treasury-context readiness, save draft, validate, attach-evidence API wiring,
and submit approval commands
from the shared DTOs while versioning, validation, persistence, private-capital fund-event context,
evidence gating, dimensional accounting normalization, period-lock enforcement, selected
ledger-book mutation checks, authenticated tenant/company scoping, and approval handoff remain
server-owned. Browser route `fundProfileId` and `ledgerBookId` values now flow into the shared
manual journal workbench query and are preserved on save, validate, submit, evidence attachment,
and lifecycle transition requests so scoped Accounting work cannot silently fall back to fund-level
manual JE drafts. The journal composer adds a sticky health and command bar, keyboard-oriented
line navigation with insert and duplicate actions, debounced governed draft autosave, and a scoped
local recovery snapshot for changes that have not reached the server. Recovery state is explicitly
labelled as non-authoritative and can be discarded back to the loaded server draft. Validation
issues retain their shared target identifiers and navigate operators to the affected header or line,
while the selected-line inspector edits the existing line entity, allocation, tax-lot, Security
Master, and shared journal dimension fields without moving accounting rules into React. The same
workstream renders the shared
private-capital activity projection as fund-event rows, capital-account aggregates, signed net
activity, ordered capital-account subledger movements with running net activity, posted fund-event
counts, ledger-impact readiness, published report-output counts, report-output readiness
candidates, report-pack workflow/publication/provenance metadata, per-event ledger-record counts,
and projection warnings rather than deriving capital-account, GL-impact, or stakeholder-package
state in React. The browser
API catalog also exposes `/api/ledger/private-capital/activity`,
`/api/ledger/private-capital/fund-event-record`,
`/api/ledger/private-capital/fund-event-command-center`,
`/api/ledger/private-capital/capital-account-subledger`,
`/api/ledger/private-capital/report-output`, `getPrivateCapitalActivity`,
`getPrivateCapitalFundEventRecord`, `getPrivateCapitalFundEventCommandCenter`,
`getPrivateCapitalCapitalAccountSubledger`, `getPrivateCapitalReportOutput`, and the Settings
backend-capability diagnostics so operators can verify the first-class private-capital review
endpoints outside the manual journal editor. Accounting fund-event ledger rows and Reporting
private-capital readiness rows expose the shared fund-event command-center route so operators can
reconstruct evidence, workflow, ledger, capital-account, treasury, reconciliation, report,
delivery, tax, and audit support from either workspace. The activity API helper accepts fund, ledger-book,
fund-event, capital-account, investor, and payment-intent filters for report-pack and cash-evidence
drill-throughs, while the direct record helpers resolve one fund event, one capital account, or one
report output to the same server-owned DTOs the aggregate projection carries. Payment-intent rows
render the shared `/api/ledger/private-capital/activity?paymentIntentId=...` route supplied by the
backend so operators open the filtered proof-chain projection instead of an ignored manual-journal
query. They also render the shared approval chain, retained bank/cash evidence with retaining
operator attribution, reconciliation links, audit events, payee, account scope, business purpose, approval policy, and retained source
evidence counts as a read-only cash-evidence drilldown while preserving the v0.18
execution-deferred posture. `/accounting/capital-accounts` consumes
`getCapitalAccountWorkbench` from the shared capital-account workbench endpoint so React renders
investor-level capital-account evidence, governed allocation policy traces, approval and replay
inputs, statement/restatement changed-line lineage, audit drill-through rows, and
live-versus-planned capability labels without browser-local accounting rules. The same workbench
now promotes source-backed fund-event command-center rows from the investor-account records, keeping
command-center, activity, and evidence routes visible beside partner capital tie-out posture.
Browser report-output rows prefer the server-built direct report-output route before falling back to
the aggregate report route. The React view model preserves
posted-event and published-output labels from the shared DTOs and renders server-owned fund-event
ledger records as the primary event-level table before the account-level capital-account subledger,
decomposed movement, GL-impact, and report-output tables. The account-level subledger table renders
the shared subledger route, opening/ending roll-forward, activity totals, approval/posting/report
counts, validation issue count, account-level readiness label/reason, next-action route, and
evidence-category readiness from
`PrivateCapitalCapitalAccountSubledgerDto`. Event-ledger rows display promoted
memo, payment/settlement reference, gross activity, capital-account opening/ending net activity,
row-count, canonical activity route, evidence-packet route, approval route when an approval id is
present, primary report route, workflow, provenance, readiness badge/reason, and next-action route
from the shared event record rather than reopening nested arrays for basic posture. The same table
renders the shared evidence-category readiness set for source support, capital-account subledger,
ledger impact, approval state, and report output so operators see the retained-evidence gaps behind
the event posture. The browser type model treats those projection arrays as non-null shared-contract
collections, so an empty private-capital state renders as empty tables instead of client-local
fallback counts.
Report-output rows also display the server-projected readiness label, reason, next action, and
next-action route from `PrivateCapitalReportOutputDto`, rather than reducing report posture to a
browser-local ready/review boolean.
This browser slice is deliberately a read/review surface for the unified private-capital event
ledger and account subledger model; React must not grow cap-table administration, broad LP portal,
native live-payment execution, full forecasting, or Backtesting Studio behavior from these rows.
The Accounting entry screen now includes a CFO / Controller close command center that derives
ready, blocked, and at-risk close posture from the latest operations-continuity workflow, retained
accounting-record evidence, reconciliation breaks, approvals, external GL provider warnings,
multi-asset valuation readiness, report-pack readiness, and close-package sign-off state. React
renders the shared status, metrics, blockers, and action rows without adding browser-local close
rules. Shared FINOPS queue rows preserve server-owned status, owner, due/SLA, severity, blocker
type, close/report impact, evidence, action, and local route labels in the browser blocker panel.
External GL provider warnings compare read-only provider evidence against Meridian-owned
ledger truth; they must not make the external GL the source of ledger authority. When QuickBooks
Online local config is ready, the Accounting GL evidence panel previews the selected company
instead of the deterministic fixture; without that config it remains on `quickbooks-fixture`.
Route-provided `fundAccountId`, `ledgerBookId`, `periodId`, and `workflowStatus` now scope the close command center's
operations-continuity lookup before workflow detail is loaded, preventing a newer unrelated close
workflow from driving the accounting cockpit when operators open a scoped Accounting route.
Close-source loading and current daily valuation schedule selection live in
`accounting-screen.close-sources.ts`; the screen retains rendering and command handling.
During workstation bootstrap, the shell lets the Accounting route render its own actionable loading
workspace. That loading view shows the route/workstream being prepared, the Accounting payload groups
still loading, and links to continuity, entity setup, provider posture, and retained report evidence
so operators are not left with only generic shell diagnostics.
Workspace navigation and command-palette root commands canonicalize caller-provided workspace
metadata to the design-document root set: `Trading`, `Portfolio`, `Accounting`, `Reporting`,
`Strategy`, `Data`, and `Settings`. Legacy root labels such as `Research`, `Governance`, and
`Data Operations` remain route aliases and internal compatibility concepts only. App-shell overview
event labels also normalize retained source names before entering the visible evidence timeline.
The browser workstation root (`/`) now opens the Daily Control Tower, a read-only shell projection
of workflow continuity, trust posture, linked context, and timestamped evidence. Its landing model
is a shell-level Home location rather than an eighth workspace: none of the seven root workspace
items is marked current (the pinned sidebar `Today` entry is), the breadcrumb heading reads
`Today`, and the operating-context copy identifies the Daily Control Tower instead
of falling back to Trading. It lists one queue grid (`buildTodayQueueRows`) that interleaves the
workspace signals (the items the sidebar badges count) with the operator triage inbox from the
shared host. Rows sort by severity, then oldest first (rows with no observed time go after dated
rows), then each source's own rank, with workspace signals first on a tie. A Source column and a
Source filter with per-source counts keep the two populations distinct. The inbox's read, filters,
bulk selection and in-flight or unconfirmed command live in `useOperatorTriageInbox`, above the
filter, so filtering inbox rows out never drops a pending command or its "Retry same command".
The landing model
now prioritizes the finance sequence Today, Exceptions, Close, Reconciliation, Ledger, Reports,
Evidence, and Data Health before non-finance surfaces. Decision drivers emphasize the finance
queue, trust posture, linked context, and evidence events before the queued work, so the first screen
explains why the operator should act, who owns the issue, what output is affected, which action is
next, and which retained evidence supports it. The queue is listed finance-first, but the headline
and the default evidence pane follow the worst queued decision, which is the one the masthead
attention pill names on every route. Below the headline, the `Workstation status` row lists the
shell attention signals (decisions, providers, provenance, workspace data, and host posture when
degraded); each signal's recovery action is in its tile behind `Details`.
Queue evidence association is fail-closed on the stable item/evidence identifier. Similar route,
workspace, severity, or list position never attaches an unrelated proof event; missing correlation
is displayed as unavailable evidence and timestamp posture.
Legacy `/overview` links redirect to that root while suffixed overview routes continue through the
retained workspace alias path.
The app shell exposes the active route as a named workbench landmark and marks that landmark busy
during bootstrap or refresh, so skip-link and screen-reader users land on the current operator
workspace with explicit loading posture.
Shared workstation primitives render visual search context as read-only textboxes and shared tab
strips move focus and the active tab stop with Arrow, Home, and End keys, so route-owned filters and
inspector tabs do not need screen-local keyboard handling.
The first-10-minutes coach mark offers task journeys for Financial Operations, Trading and
Portfolio, Strategy and Research, and Administration. Journey progress is route-backed and retained
in the existing versioned onboarding storage record; switching journeys preserves completed work.
Supporting and metadata typography has a 12px floor in the dense setting. Global accessibility
styles retain visible focus, selected/current state, status boundaries, and data-table structure in
Windows forced-colors mode; reduced-motion mode suppresses non-essential animation and transitions.
The app shell keeps cross-workspace ranking and disclosure chrome centralized, while workspace
linked-context builders, workspace operator-focus candidate construction, workspace
evidence-timeline projection, workflow-continuity trail definitions, trail selection, active-route
matching, primary workflow routing, operating-scope route/query helpers, and workspace
workflow-continuity status builders live outside the shell so quote, exposure, close, break,
automation, reconciliation, provider, run, and report-pack recipient state can evolve with their
owning routes instead of accumulating in `app-shell.view-model.ts`.
The shell workflow-continuity view model now lives in
`app-shell.workflow-continuity-view-model.ts`; `app-shell.view-model.ts` imports it as a coordinator
boundary instead of importing each route-specific continuity, linked-context, operator-focus, and
evidence-timeline helper directly.
Workflow-continuity view-model contracts live in `app-shell.workflow-continuity-types.ts`, so the
route coordinator no longer owns the cross-workspace workflow, decision brief, focus, linked
context, and evidence timeline type definitions that are implemented by the workflow-continuity
builder.
The shell command-palette trigger, route-focus announcement model, no-host demo-data notice, status
panel, and trust strip live in `app-shell.command-palette.ts`, `app-shell.route-focus.ts`,
`app-shell.development-fixture-notice.ts`, `app-shell.status-panel.ts`, and
`app-shell.trust-strip.ts`, keeping keyboard shortcut semantics, active-route focus copy, demo
evidence-path steps, bootstrap recovery copy, failed-workspace items, build/mode posture, source
posture, and provider posture out of the route coordinator while preserving the same app-shell
view-state contract for React.
The shell header shows the operating scope as `OperatingScopeBar`; the on-demand workflow detail
(current workflow, decision and step links) is `WorkflowContextDisclosure` in
`components/meridian/workflow-context-disclosure.tsx`, styled by
`src/styles/workflow-context-disclosure.css` and shown inside the scope picker. The former
`WorkflowContinuityDock`, which combined scope chips and that disclosure in one header control, was
removed once the scope bar replaced it.
Workspace navigation rail and drawer styles live in `src/styles/workspace-nav.css`, imported by
`components/meridian/workspace-nav.tsx`, so root-workspace routing, preserved operating-scope chips,
expand/collapse controls, status badges, and responsive drawer variants stay with the navigation
component instead of the global workstation stylesheet.
App-shell frame, skip-link, masthead, command-search trigger, trust-strip, session card, startup
status, status-strip, and workbench scroll styles live in `src/styles/app-shell.css`, imported by
`app.tsx`, so the global workstation stylesheet no longer owns root shell chrome.
The final light-first workspace surface cascade now lives in `src/styles/workspace-surface.css`,
imported immediately after `src/styles/index.css` in `main.tsx`, so `index.css` stays focused on
global tokens, Tailwind layers, and legacy shared rules while the workspace surface overrides remain
order-pinned and reviewable.
Tailwind 4 compilation uses `@tailwindcss/postcss`; the stylesheet imports `tailwindcss` and
explicitly loads `tailwind.config.ts` with `@config`. Automatic monorepo scanning is disabled with
`source(none)` so the config's existing dashboard content globs own utility discovery. Keep the
Segoe UI/Cascadia font stacks, semantic colors, and radius/shadow tokens in that shared config.
Tailwind handles CSS imports and vendor prefixes, so a separate Autoprefixer plugin is unnecessary.
The pre-module startup CSS in `index.html` belongs to the base layer so it yields to the loaded
workstation, and base button cursors and placeholder colors preserve the existing defaults.
Focus utilities use `outline-hidden` to retain forced-colors outlines; `shadow-xs`,
`backdrop-blur-sm`, and `shrink-0` retain their previous visual and flex behavior under v4.
Semantic badges retain their unfilled backgrounds: Tailwind 3's opacity scale stopped at multiples
of five, so the `/8` and `/12` color utilities never compiled and the surfaces naming them have
always been unfilled. Tailwind 4 accepts any percentage, so keeping those utilities would introduce
new tints and reduce text contrast rather than preserve the current appearance. Every other opacity
modifier in the dashboard is a multiple of five and compiles identically under both majors.
Card-title defaults live in the component layer so caller-supplied text-size utilities still win
independently of Tailwind utility ordering.
`src/tailwind-build.test.ts` compiles the real stylesheet through the configured PostCSS pipeline
and checks token-backed components, responsive utilities, typography, opacity, and focus behavior.
Any future separately processed stylesheet that uses `@apply` must first reference
`src/styles/index.css` with `@reference` to access the workstation theme without emitting it twice.
### Meridian Design System 4.3.0 integration

Setup and activation surfaces use the canonical surface, ink, status, radius and chrome
roles. Their provider recovery, cancellation, progress and navigation behavior remains
owned by the existing first-run model.

Native date adapters retain ISO civil dates across time zones, expose labels and validation
on their actual controls, and support calendar keyboard navigation inside dialogs. Selection
controls retain one tab stop and move DOM focus with their arrow/Home/End commands.
Current-view printing preserves the mounted financial rows, selected context, session and
provenance without fetching offscreen records; governed report generation remains a separate workflow.

The root `Meridian Design System/` package is the visual source bundle for tokens,
component references, patterns, templates, and governance scripts. The browser workstation consumes
the package through copied `src/assets/` files and the `src/design-system/assets.ts` bridge, while
`src/design-system-contract.test.ts` keeps the package manifest, canonical token values, asset bridge,
and runtime CSS alignment under test.
Native adapters use complete workstation color values such as `--border-color` and `--ws-border`.
Tailwind role variables such as `--border`, `--primary`, `--danger` and `--warning` contain HSL
channels; direct CSS color values must wrap them in `hsl(...)`. The source contract in
`src/design-system-color-values.test.ts` prevents raw channel values from reaching native CSS or
arbitrary utilities. `node scripts/test-native-adapter-colors.mjs` checks computed native borders,
chart separators, validation colors and keyboard focus in light and dark Chromium appearances.
Set `MERIDIAN_CHROMIUM_PATH` when using a system Chromium installation.

Workstation tokens in `src/styles/index.css` follow the package's 4.3.0 roles: graphite (`#29323A`)
is the primary action and chrome color, terracotta (`#C56343`) is the brand/scenario signal only,
and focus and selection use the interaction blue family (`#2D6FB3` ring, `#E7EDF2` selected
surface). Light, auto-dark, manual-dark, and forced-light scopes take their values from the package's
`tokens/` files.

`main.tsx` loads `src/design-system/fonts.css`, synchronized from the package,
before the workstation stylesheet. The package ships IBM Plex Sans and Mono as TTF; the sync serves
them as WOFF2 under `src/assets/fonts/` (about a third of the size) and rewrites the projected
`fonts.css` sources to match. `woff2-sources.json` records each source TTF and WOFF2 SHA-256, so check
mode stays pure Node while `npm run design-system:sync` reconverts changed fonts through
`scripts/convert-fonts-to-woff2.py` (Python with `fonttools` and `brotli`; set `MERIDIAN_PYTHON` to
pick an interpreter). Their OFL notices ship through `public/licenses/`. Brand
marks, lockups, the identity sheet, and module icons use the canonical copied
assets in `src/assets/` and the typed asset bridge. Vite bundles these local
assets for hosted and offline-capable deployments. Assets linked by `index.html`, including
the favicon, remain emitted files when JavaScript imports them too; the URL does not depend
on which build pipeline processes the shared asset first. Other assets retain Vite's size-based
inlining behavior. `src/vite-config.assets.test.ts` covers the JavaScript-first case.

Asset drift is checked before dev/build and by Vite's build hook. Run
`npm run design-system:check` to verify the copied bytes,
`npm run design-system:sync` after an intentional package asset update, and
`npm run design-system:test-assets` for the synchronization regression suite.
Keep the package, synchronized assets, token bridge, and contract tests in the
same reviewable change.

Token values are checked from the package directory. `python3 scripts/check_token_parity.py`
fails when a token name that `src/styles/index.css` and the package both declare resolves to a
different value in light, auto-dark, manual-dark, or forced-light scope; a deliberate difference
has to be listed in `tokens/parity-manifest.json` (none is). The Review severity chip is the
terracotta signal, distinct from Action's ochre, with `--brand-signal-text` as its text ink.
`python3 scripts/check_contrast.py` checks chip and button contrast, `var()` fallback literals,
brand art in all three tracks, and the WPF runtime palette.

Auxiliary route and primitive styles consume the same roles: `--font-body` /
`--font-display` for interface text, `--font-data` for values and identifiers,
`--interaction-focus` for keyboard focus, `--interaction-selected` and
`--interaction-selected-surface` for committed selection, and
`--interaction-link` for navigation links. Table headers, zebra rows, row detail,
popovers, and shell fields use the active `--ws-*` / chrome surfaces so appearance
changes do not reveal hardcoded legacy panels. Financial and permission states
retain their own severity roles and visible labels.

Primary hover/pressed states remain structural. The Paper environment uses Field
slate, and chart observations/comparisons use graphite/slate/neutral roles. The
host-served distribution also preserves OFL notices under `notices/`, alongside
the compatible `licenses/` path. Canonical workspace SVGs serve as masks so
navigation color remains legible in dark and forced-color modes. Startup and
chart labels use the shared font roles; density remains operator-controlled.

Live shell chrome and Accounting adapters remain dashboard-native TypeScript: `WorkstationTopbar`,
`WorkstationStatusBar`, `TrialBalanceTable`, `AgingTable`, and `ReconciliationComparisonPanel`
adapt the manifest-backed design-system references without importing root JSX or runtime-injected
package CSS into the dashboard build.

Four components new in 4.3 are also dashboard-native ports wired into live screens; each renders
only from data the screen already loads:

- `LocalDate` / `LocalDateText` (`src/lib/local-date.ts`, `src/components/ui/local-date-text.tsx`)
  carry and render calendar dates in the Data corporate-action inbox and the Accounting
  prepare-next-period dates, and `utcToday()` names the UTC "today" default used by the Reporting
  as-of defaults and the Settings asset-profile effective dates. Visible text is unchanged.
- `SelectionCompare` (`src/components/charts/SelectionCompare.tsx`, with `ComparisonPlate` /
  `PlateGrid`) adds a per-row `Chart` toggle, an inline tray capped at six runs, and an inline sheet
  of equity paths on one shared scale to the Strategy run library. The set is chart-only and
  separate from the two-run Compare/Diff/Promote selection. Curves come from the existing run
  equity-curve client when the sheet opens; a run without a curve keeps its plate and says why.
  Paths are rebased to 100 at each run's first curve point, and Total return is the run record's
  figure. The package's overlay sheet and corner-docked tray are not ported.
  Strategy's equity-path component and state live in `strategy-screen.equity-paths.tsx` and
  `strategy-screen.equity-paths.view-model.ts`; the run-library view model re-exports its existing
  equity-path API for compatibility. This keeps chart selection, curve loading, and shared-scale
  presentation cohesive without growing the main screen and command view model.
- `RollforwardTrace` and `TieOutPanel` (`src/components/accounting/`, arithmetic in
  `proof-arithmetic.ts`) are read-only. The Capital Account workbench proves each investor row from
  its subledger entries' net capital activity and reports an account with no entries as
  "Not itemised", never as tied. The Accounting reconciliation band mounts the tie-out as the proof
  of the figures `ReconciliationComparisonPanel` already shows, with status passed explicitly so
  offsetting breaks never read as tied. Accept, resolve, escalate, reopen, and add-item actions and
  the reviewer line are omitted because no endpoint backs them. The external GL reconciliation is read
  on the External GL workstream, so the tie-out appears in the Reconciliation band after that
  workstream has been opened in the same Accounting session.
- `OptimisticCell` (`src/components/data/optimistic-cell.tsx`, with `OptimisticProvider`,
  `OptimisticRowBar`, and `EditableCell`) edits provider module priority in place on the Settings
  provider card. The write is the existing provider-module update and always resends the module's
  `enabled` flag. An edit reads Provisional, then Confirmed (saved, not live until the provider host
  restarts); a server refusal reads Rejected with the server's reason; a write with no answer is
  read back and otherwise shown as Unlanded. Revert is a second write of the prior value under a
  reason held in this browser session only, because the provider API has no reason or audit field.
  The edit drawer remains the full-form path.
Cash Ladder uses the shared `Select` for scenarios and `Input type="number"` for the minimum
balance draft. Blank input still requests the service default; Enter and Apply threshold keep
the existing request and account-reset behavior. The provider integration workbench uses shared
`Select` and `TextArea` controls for template/capability choices and JSON/CSV drafts, preserving
the existing labels, disabled states, validation, and service-owned activation workflow.
The shared `Tabs` primitive generates instance-specific tab/panel ids, labels each panel from its
tab, and includes the visible panel in keyboard focus order. Explicit `panelId` links are retained;
reusing tab values across independent groups no longer creates duplicate generated ids.
Market Data's URL-backed tab changes retain tab focus; operating-scope and evidence-hash navigation
still use the shell's route focus handoff.
Metric cards resolve the active workstation surface and border roles, including nested light scopes.
Nonselectable and empty dense tables provide named keyboard stops for scrolling. Cash Ladder's
interactive chart exposes a labelled group and pressed bucket controls with visible keyboard focus.

The merged PR also carries WPF IBM Plex Sans/Mono typography through its native
font resources. That concurrent platform work is preserved. Browser checks here
cover the web workstation; Windows/WPF rendering and identical cross-platform
metrics have not been established by those checks.

Run `npm run smoke:design-system` against a dev server started with
`MERIDIAN_DEV_MODE=fixture-only` to reproduce the rendered integration checks.
The script accepts `--url` and `--output`; `CHROMIUM_EXECUTABLE_PATH` selects a
system Chromium when needed. Its report and screenshots record theme values,
loaded fonts, workspace navigation, proof-dialog keyboard behavior, density,
native Accounting borders/selection, and fresh accessibility scans. Fixture API
gaps remain explicit in the report and are not backend validation.

The full PR #3501 adoption review is recorded in
`Meridian Design System/api/project-compatibility.json`. It covers every browser
screen and shared adapter, including concrete reasons for retaining specialized
accounting tables, native file controls and embedded row actions. The dashboard
keeps its existing React, routes, service contracts and financial semantics.

Provider institution search now uses shared search input and selectable dense
results while retaining explicit server search and exact institution identifiers.
Maintenance history uses a shared filter and the read-only dense adapter through
the governed TableWorkbench. Its default remains service order with a 25-row
limit; concurrent personal-view, search and export controls are preserved. Report-writer token actions
use shared buttons with visible keyboard focus and disabled reasons, preserving
reordering, removal and drag behavior. Other compatible forms and flat tables
use the same adapters; specialized financial tables retain their row/header and
grouping semantics. `DenseDataTable.tableClassName` preserves a screen's existing
minimum table width without introducing a row cap or sort.

Reproduce package-wide offline validation from the design-system directory with
`npm run test:library` and `npm run test:types`; the package's test guide documents
the source-level interaction suites. These synthetic checks establish component
and browser behavior. Live providers, backend writes, native Windows/Edge and
assistive-technology announcement journeys require their own evidence.

`node scripts/smoke-design-system-adoption.mjs --url http://127.0.0.1:5173/workstation --output /tmp/meridian-adoption`
reproduces the institution, maintenance and report-token acceptance matrix against
the fixture-only server. Its defaults run both themes at three desktop sizes and
a 200% desktop zoom layout equivalent, with exact request/selection/cancellation
IDs, keyboard ordering, drag actions and scoped accessibility checks. The optional
`--only` and `--sizes` filters produce explicitly focused evidence.

The final adoption checkpoint passes 5,100 tests across all 455 dashboard test
files, strict types, production build, 31 tooling checks and ratchets. Lint has
zero errors and 98 existing warnings. The maintained browser passes 64 checks
and 15 axe scans; the three adoption journeys pass 24 checks and 24 scoped axe
scans. All use synthetic fixtures; these counts do not certify backend services
or native Windows/assistive-technology behavior.

The routes that walk reads are answered by `src/lib/dev-fixtures/`. Read surfaces
that once returned 501 there (reconciliation readiness, statement-run detail for
the sample run, operator preference reads, ledger books, the provider capability
matrix, and the empty-state answers for escalations, valuation schedules, data
quality and the corporate-action inbox) are served from
`workstation-surface-fixtures.ts`, typed against the shared contracts. The smoke
script lists those paths and fails if one returns 501 again. Three calls stay
unanswered on purpose and are still reported: the financial-operations command
center and the close period plan (large composite projections with no synthetic
fixture yet), and the POST that records an operator visit (a write). The walk
covers a small part of the workstation: other routes, including most of Data and
Security Master reference data, still return 501 in fixture-only mode.

### Workspace-specific styling

Accounting uses one local navigation model in the workspace sidebar. Unique destinations are grouped
under Close, Records, Reconciliation, Review, and Administration; Accounting screens do not repeat
those routes in a horizontal tab strip or task-mode launcher. Governed delivery evidence remains a
Reporting handoff at `/reporting/evidence`, while `/accounting/reporting` is retained only as a
non-navigable compatibility route.
Accounting-specific split-pane, reference-panel, and journal-entry workstation styles live in
`src/styles/accounting-screen.css`, imported by `accounting-screen.tsx`, keeping route styling out of
the shared workstation stylesheet.
Command palette shell, chip, status, and group styles live in `src/styles/command-palette.css`,
imported after shared tokens in `main.tsx`, so the global workstation stylesheet no longer owns the
palette overlay rules.
Workspace filter bar, tab strip, inspector host, and document canvas primitive styles live in
`src/styles/workspace-primitives.css`, imported by `workspace-primitives.tsx`, so the global
workstation stylesheet does not own primitive-specific accessibility surface styling.
`WorkspaceTabStrip` emits stable tab ids from each panel id or an explicit `tabId`, and
`WorkspaceTabPanel` centralizes `role="tabpanel"`, `aria-labelledby`, focus, and hidden-state
semantics for route tabs that expose richer keyboard behavior.
Shared toolbar strip, dense data table, and entity-summary primitive styles live in
`src/styles/ui-kit-primitives.css`, imported by `ui-kit-primitives.tsx`, keeping dense table
keyboard/accessibility behavior and visual ownership in the same component module.
Dense row-detail panel styles live in `src/styles/dense-row-detail-accessibility.css`, imported by
`dense-row-detail-accessibility.tsx`, so row/detail focus handoff, labelled regions, selected-source
badges, and panel chrome stay owned by the accessibility primitive instead of the global stylesheet.
Reporting exposes route-owned task modes as Daily Reporting Cockpit, Report Builder, Run Status,
Delivery Evidence, Exports, and Governance. `/reporting` is the Daily Reporting Cockpit landing
route and stops at the daily decision queue plus focused task-mode links instead of rendering the
full builder surface. `/reporting/report-builder` owns governed output design and schedules,
`/reporting/run-status` owns queue posture, `/reporting/report-packs` keeps the report-pack
approval workflow panel while presenting as Delivery Evidence, `/reporting/exports` owns governed
export artifacts, and `/reporting/governance` owns access, approval, lifecycle, and audit controls.
Each queued daily item exposes blocked status, owner, affected output, next action, and proof or
evidence posture so browser Reporting matches the WPF cockpit decision model.
The Reporting task-mode resolver, report-pack route detector, and launcher links live in
`reporting-screen.task-mode-view-model.ts`, keeping the daily-cockpit IA out of the broader
reporting view model.
Financial Record Explorer drawers now render the shared Number Passport component for the selected
record, carrying source, freshness, reconciliation, approvals, report usage, blockers, evidence
packet, and audit-trail facts through the Accounting, Portfolio, Security Instrument, and Reporting
provenance surfaces. DTO-backed explorers also restore `frexExplorer`, `frexView`, `frexSearch`,
`frexFilter`, and `frexRecord` query state, emit a `Share state` link whose accessible name names
the selected saved view, search text, explicit filters, and proof record when present, and require
operators to supply stable saved-view names instead of timestamp-only labels, keeping browser FREX
review URLs portable without duplicating saved-view storage in React.
FREX route-query parsing, filter restoration, share-link serialization, and accessible share-state
summaries live in `financial-record-explorer.view-state.ts` so the React shell stays focused on DTO
rendering and Number Passport proof presentation.

The browser type mirrors include additive instrument-role, book-position, economic-state,
economic-event, projection-lineage, authoritative book-context assertion, and existing rule-pack
reference shapes. Candidate, posting-command, and Asset Operations payload fields remain optional
for older JSON. The browser submits posting intent and assertions only; server services resolve
authority, and the same shared contracts remain available to WPF without browser-owned accounting
logic or a new route.

`ui-api-routes.generated.ts` includes `SecurityMasterAssetClasses` and
`SecurityMasterAssetClassDefinition`. `SecurityIdentifierKind` includes `LoanX` and `Clip`, and the regulatory derivatives kinds `Uti`, `Upi`, `MarkitRedEntity` and `MarkitRedPair`.

The passport editor builds its term picker from `GET /api/security-master/asset-classes/{assetClass}`
(`security-master-schema.api.ts`): scalar-editable terms grouped as the registry declares them,
a value control per editor kind, and a client pre-check
(`security-passport-schema-fields.view-model.ts`). The free-text field path stays as a fallback.
The pre-check mirrors the shared `SecurityAssetTermFormModel` and is pinned to it by a parity
contract: `SecurityAssetTermFormParityFixtureTests` (tests/Meridian.Tests) generates
`src/test/fixtures/security-asset-term-form-parity.json` from the registry and form model, and
`security-passport-schema-fields.parity.test.ts` replays it through the mirror — field sets in
declared order, value choices, and every validation verdict over a fixed corpus. The server-side
governed validator remains authoritative.

## Security Master publish outcome

`SecurityMasterPublishResult` in `lib/api/security-master-workbench.api.ts` carries an optional `canonicalMerge`. It says whether publishing the approved edit changed the golden record (`Merged` or `AlreadyCanonical`) or not (`OverlayOnly` or `NotApplicable`). When an approved proposal is stale because its field changed after the draft, the publish route answers 409 with `stale-field-proposal`. The existing classifier reports that as a revision-state conflict carrying the server message.

Statement run detail, capital accounts and the governed-approvals poll ignore a response that
arrives after a newer request was issued. Capital accounts shows its loading banner, never the
previous scope's rows, while a changed query loads.

## Security Master declared exercise outcome

`SecurityMasterScheduleSummary` and `SecurityMasterScheduleBook` (`types/workstation-6.ts`, `types/workstation-7.ts`) carry an optional `declaredExercise`: the typed outcome of a record's declared call and put schedule (`status` of `Resolved` or `Blocked`, `asOf`, `blockers` with a `code` and `reason`). It is absent for a record that declares no schedule. No screen reads it yet; the passport statuses the screens already render are what change when a schedule is blocked.

## Analytical investigation views

These views extend existing workspaces and retain their tables and inspectors. They are read-only
investigation surfaces; chart selection does not grant trading, posting, election or approval authority.

| Existing surface | View and interpretation |
| --- | --- |
| Trading / Risk | Execution journey joins exact order aliases to retained audit decisions and genuine session fill reports. Expand events for source, actor, event/recording-time basis and identity. Source availability, truncation, unknown stages and elapsed gaps remain explicit. Completed-order summaries are not individual fills. |
| Data / Quotes | Market-depth quantity illustration freezes an identified snapshot and highlights the chosen side, partial final level and uncovered quantity. Beyond the returned ladder means **beyond displayed depth**, not exhausted market liquidity or predicted execution. This does not seed an order ticket. |
| Strategy / Covered call | The screening map plots DTE and delta and synchronizes contract selection with the chain inspector. Boundaries describe captured preview filters; other requirements remain visible. Unknown measurements, partial expiry coverage and snapshot times remain inspectable. |
| Accounting / Margin control | Currency-separated paired maintenance-margin dots and provider excess-liquidity bars retain broker/provider authority, diagnostic model names, evidence dates and missing values. Zero is the explicit deficit boundary. |
| Portfolio / Loan book | Contractual maturity buckets show current principal outstanding separately by currency, with overdue/unknown buckets and exact facility drilldown. They are not scheduled repayments, undrawn commitments or a refinancing forecast. |
| Portfolio / Family office | The directed ownership map consumes the supplied graph and effective dates, distinguishes Owns from advisory/custody/registered-holder links, and highlights legal paths for selected assets. Structure-date changes do not relabel current financial balances. Unknown percentages, cycles and missing endpoints remain visible; economic exposure is not inferred. |
| Portfolio / Family office | Private-capital lifecycle rows keep commitment, called, unfunded, distributions and NAV separate, group by strategy/vintage and retain currency, availability, exact amounts and source/activity drilldown. Distributions and remaining NAV are never an additive commitment stack. |
| Security instrument / Bond analytics | The callable-bond horizon uses server-authored maturity/exercise scenarios and governing yield-to-worst ties. Holder puts remain separate from issuer-call YTW. The payment strip separates interest/principal and links exact-value details. Rates are annual fractions and amounts are per 100 face. Notice deadlines do not establish an actual call. |
| Data / Corporate-action inbox | The weekly security/date calendar distinguishes ex, record, payment and actual retained election cutoffs. Select an event to open its exact case. Election timestamps retain offsets/source evidence; ex-date and case SLA dates never become election deadlines. |
| Accounting / Selected journal disposal tax results | The parcel scatterplot uses retained holding days and supplied tax character. Selection preserves exact economic/recognized gain, deferred loss and provisional/settled evidence. This is journal-scoped; it does not imply portfolio-wide tax aggregation. |
| Strategy / Promotions | The robustness landscape compares retained sweep points across two numeric axes and a fixed remaining-parameter slice. Metric/threshold controls expose acceptable regions, missing/failed/repeated cells and selected-run evidence. It is in-sample sensitivity unless separate out-of-sample evidence exists; no new sweep-launch or promotion authority is introduced. |

Each view provides keyboard operation and an inspectable table/detail alternative; color is
supplementary to labels and symbols. Source failures, unsupported calculations and missing values
remain visible instead of becoming zero. Existing tokens, SVG and semantic HTML are used without
new chart dependencies. Scope changes invalidate in-flight reads and selected evidence where applicable.

Explicit fixture-only demonstration routes live in `src/lib/dev-fixtures/analytical-fixtures.ts`.
They demonstrate currency separation, historical relationship filtering, missing depth/evidence and
parameter-grid gaps using synthetic records. They do not replace failed authoritative reads.
The private-capital retained source and election-calendar companion formats are documented in
[`Meridian.Ui.Shared`](../../Meridian.Ui.Shared/README.md#analytical-read-model-evidence).

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in
`docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-DASHBOARD -->
| Roadmap item | Title |
| --- | --- |
| `W14-ATTEST-001` | Single-principal attestation for governed approvals in the single-operator envelope |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `W5X-EVIDENCE-001` | Evidence Vault productization |
| `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `W12-ENTITY-001` | Effective entity lifecycle and audited book-control migration |
| `W12-ICPAIR-001` | Durable reviewed counterpart proposals and aged intercompany breaks |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-DASHBOARD-001` | Add browser workstation route diagram coverage for paper readiness | done | medium |
| `TODO-SRC-UI-DASHBOARD-002` | Wire Accounting Transaction Lab browser endpoint requests to shared API contracts | done | high |
<!-- source-todos:end -->

## Design-system development

From the repository root, `npm run check:design-system` runs the focused integration
checks without installing dependencies, rewriting baselines, regenerating checked-in assets, or starting
a browser. Use the Node 24 version selected by `.nvmrc`, install both the dashboard and
`Meridian Design System/` dependencies with their committed lockfiles, and provide Python 3
(`MERIDIAN_PYTHON` can select its executable). The command rejects stale direct dependencies
before running consistency, typography, component, asset, gallery, action-contrast, package
freshness, and export-integrity checks. It is a focused development check; the full application
and broader legacy package-governance checks retain their own scope and results.

`npm run ratchets:update` from this directory records reductions in existing screen-level UI
debt. It refuses new offenders or per-file increases and preserves the baseline on failure.

Start `npm run dev:fixtures` from the repository root and open
`http://127.0.0.1:5173/workstation/__design-system` to inspect the native component workbench.
It is available only in fixture-only Vite development, before application data providers mount;
production and backend-connected development do not expose it. The workbench uses the actual
Button, Badge/status, form, table, and inspector adapters with fixed synthetic records. Appearance,
density, explicit-light surfaces, data states, and button states live in its shareable URL, with
a Reset control. It makes no backend requests and does not use operator preference storage.

Browser integration verification is explicit: `npm run check:design-system:browser` from the
repository root runs the maintained design-system smoke profile. Use its `--help` output for
server, URL, and evidence-directory options. The profile exercises real fixture-backed routes
and the workbench, including font loading, selected-state contrast, keyboard focus, appearance,
density, and asset health; failures retain machine-readable results and screenshots.

The exported package's `tests/gallery.html` reads its specimen inventory directly from
`_ds_manifest.json`, with metadata, filters, links, and one optional preview at a time. A loaded
specimen document is not a conformance verdict: some specimens retain their Design Component or
external-runtime requirements. Package conformance and browser regression harnesses remain separate.

## Validation

Launcher/process ownership coverage runs from the repository root with
`node --test scripts/dev/web-dev.test.mjs`. Focused Vite/API mode coverage is in
`src/vite-config.test.ts` and `src/lib/api.development-modes.test.ts`.
Shared scenario coverage runs with:

```bash
npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/scenarios/scenarios.test.tsx src/vite-config.scenarios.test.ts
node --test scripts/dev/web-screenshot-scenarios.test.mjs
```

```bash
npm --prefix src/Meridian.Ui/dashboard run lint
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
npm --prefix src/Meridian.Ui/dashboard run smoke:workstation
```

Linting is a correctness-only ESLint flat-config baseline (`eslint.config.mjs`): typescript-eslint
recommended, react-hooks rules, and a local kebab-case filename rule with grandfathered
PascalCase/camelCase directories (`components/accounting`, `components/charts`,
`features/accounting`). No stylistic or formatting ESLint rules are enforced. `react-hooks/exhaustive-deps`
stays a warning; treat new warnings in touched files as part of the change. After ESLint,
`npm run lint` runs `scripts/check-typography-floor.mjs`, a ratchet on the 12px type floor. It
counts sub-12px text per file: Tailwind `text-[Npx]` arbitrary sizes and CSS `font-size` below
12px, excluding `*.test.ts(x)` fixtures. A file that gains occurrences, or a new file with any,
fails against `scripts/typography-floor-baseline.json`. Run the script with `--update-baseline`
after a reduction; it refuses any increase, so the baseline only shrinks. Screen-level
accessibility is exercised by the `src/screens/*.a11y.test.tsx` suites plus axe assertions embedded
in the larger screen tests; keep new screens covered by at least one axe render.

## Change rules

Do not create mobile-first workflows or native mobile clients. Prefer shared read models and
endpoint contracts for behavior also consumed by WPF or host workflows.

## Related docs

- `src/Meridian.Ui/README.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/desktop-layers.md`
- `docs/source/generated/source-module-index.md`
- `docs/reference/accounting-report-packs.md`
- `docs/operators/governed-reporting-operations.md`

Browser reconciliation route helpers include the shared Accounting casework family for assignment, lifecycle transitions, comments, taxonomy, sign-off, reopen, audit, bulk triage, and bulk status/result lookup; keep these helpers aligned with `UiApiRoutes` and WPF consumers.

Browser extensibility route helpers expose the shared core extensibility catalog plus tenant-template activation-readiness, activation, and activation-history endpoints; keep `src/types.ts` aligned with `Meridian.Contracts.Extensibility` instead of adding UI-local workflow or rule shapes.

## Accounting close browser surface

The External GL route also hosts bounded accounting onboarding for one entity, explicit account
population and date range. Its shared onboarding API retains criteria revisions, exact import and
mapping identities, comparison snapshots, difference ownership, independent reviewer decisions and
frozen readiness packets. Historical comparisons can be reproduced from retained inputs. Readiness
shows unresolved differences across every required date; a later clean period does not clear an
earlier required period. The browser never substitutes fixtures for these authoritative reads or
writes, computes readiness, imports missing sources automatically, or grants accounting authority.
Focused workflow, stale-response and accessibility coverage lives in
`accounting-screen.onboarding-panel.test.tsx`; API boundary coverage lives in
`lib/api/accounting-onboarding.api.test.ts`.

The Accounting route reuses fund-operations ledger views and now includes trial-balance source-event and approval drill-through affordances. Keep browser-only rendering in `src/screens/accounting-screen.tsx` and shared accounting close contracts in `src/features/accounting/accountingCloseModels.ts`.

Accounting Ledger Explorer also includes the first W10-CONSOL-001 consolidation slice. Operators
preview an authoritative effective-dated ownership perimeter, inspect gross/proposed/posted and
consolidated account balances, retain unmatched reciprocal differences, and create drafts for the
existing journal approval workflow. Source disclosures show the exact posting entity, counterparty,
journal, book, line, debit, credit, and effective date; correction links and renewed-review status stay
visible. Changing scope discards prior results, and failed requests require another preview. All
amounts and eligibility are server-owned. This slice supports exactly two directly wholly owned
entities, Primary basis, one shared functional currency, and a dedicated elimination book; it excludes
foreign currency translation, minority interests, and nested ownership. Browser regression coverage
is in `accounting-screen.consolidation-panel.test.tsx`.

The close workspace (`/accounting`) exposes **Prepare next period**. Operators select an
authoritative source plan, capture or reuse a retained template version, specify each task's
calendar rule, select an authoritative target book and period, and resolve owner and policy
changes before previewing. Calendar dates, dependency preservation, fresh evidence and sign-off
requirements, stale-preview checks, and duplicate prevention are owned by shared close services.
The browser never calculates rollover deadlines or copies completed controls. Prior completions,
approvals, reviewed evidence, journal references and locks remain on their original period.

All preparation reads and writes disable development fallback. Target-book discovery uses the
authoritative source book's fund identity; stale or mismatched responses are discarded. A lost or
interrupted creation response is retried with the same preview and idempotency key. A typed stale
or missing preview response requires a new preview. Before creation, the browser saves only the
opaque request identity in session storage. Reload and navigation recover that same request after
fresh authorized source, template and book reads; no cached approval or preview state is reused.
The creation result retains template version and history
and links back to the prepared close scope. Reloaded plans expose their retained preparation
lineage and creation history through the shared plan configuration. Focused coverage lives in
`accounting-screen.prepare-next-period.test.tsx` and `close-preparation.api.test.ts`.


## Reconciliation queue and inspector

`/accounting/reconciliation` leads with a reconciliation-run picker, statement-import evidence access, and a break queue beside the selected
break inspector. Assignment, resolve and dismiss controls belong to the selected record, before
its supporting facts. Existing service permission checks, operator identity, rationale validation,
casework outcomes and financial calculations remain authoritative. Exceptions reuses the queue
pattern without constraining its cross-run casework to one run.

`accounting-screen.reconciliation-workbench.tsx` owns the work surface.
`accounting-screen.reconciliation-resolve-dialog.ts` owns rationale and confirmation state;
existing view-model exports remain compatible. Supporting account
oversight, calibration/history, readiness, source comparison and run evidence remain available in
labelled disclosures. Statement import IDs and strategy reconciliation run IDs remain distinct;
the UI does not infer a mapping between them.

`reconciliation-view-state.ts` stores `reconciliationRun`, `reconciliationBreak`,
`reconciliationSearch`, `reconciliationStatus` and `reconciliationSort` in the URL, preserving
shared operating scope, unrelated query parameters and the hash. Search replaces the current
entry; discrete choices create history entries. Clear filters clears search/status while retaining
sort. Queue scroll lives on the browser history entry and restores after asynchronous rows arrive.
Screen-local controls retain keyboard focus instead of triggering shell navigation focus.

Filtering preserves the selected inspector even when its row is outside the results. A missing
explicit selection remains unavailable with a notice instead of silently selecting another record.
Scope revisions invalidate old rows and pending results, including A–B–A navigation.
Committed same-scope outcomes update the queue across run changes while obsolete action notices
stay suppressed. Pending or
failed queue reads disable actions; view state never stores financial authority or approvals.
Urgency sorting uses the supplied SLA state and case priority, not displayed amounts or currency.

At constrained browser widths, Inspect selected break and Back to break queue switch between
surfaces. Enter/Space on a break row opens its inspector; Escape restores queue focus. Statement
run activation retains its own detail target. Table overflow stays within the queue.

The shared shell's `WorkspaceStatus` exposes affected areas in a compact disclosure and disables
Retry while recovery is in progress. Scope announcements distinguish applied dimensions from
those retained for other workflows. The seven root workspaces and WPF remain unchanged.

Focused regression coverage includes `reconciliation-view-state.test.tsx`,
`accounting-screen.reconciliation-continuity.test.ts`, `accounting-screen.reconciliation-workbench.test.tsx`,
`app-shell.route-focus.test.ts`, and the existing Accounting, shell and accessibility suites.

## Native commitments and capital calls

Accounting → Capital accounts links to the native commitment-to-cash workbench at
`/accounting/capital-calls`. Select a retained ledger book; the server resolves entity, fund,
currency, tenant and company authority. Reads disable development fallback and explicitly
report unavailable persistence or unsupported book scope.

Commitment revisions retain source terms and evidence before explicit review and approval.
Call previews retain the reviewed server plan hash. The notice date selects one effective
approved revision per commitment; inactive successors are excluded without reviving older terms.
Changing that date clears selection and prior previews.
Issuance and funding only queue governed journal drafts. Posted balances and receipt identities
come from the shared authority. Decimal text is preserved without browser allocation or rounding.
Retained calls and their detail show an overdue receivable only when a posted outstanding balance
remains after the due date, evaluated at the server view's UTC date rather than the local clock.
Funding requires a distinct receipt identity, bank-line identity and retained bank evidence.
The cash-receipt panel imports a reviewed BankEvidence/BankStatement JSON observation through
the native book-scoped authority. It submits only the accepted Evidence Vault reference and
document ID. The returned source fills bank-line identity, exact amount, effective date and
evidence; a fresh match confirmation is required before the separate funding-draft action.
Import conflicts preserve existing cash fields and never create local balances or journals.
The bounded funding workflow uses the call's accounting period; closed-period or different-period
receipts require a separately supported correction or receipt workflow and remain blocked here.
Notice generation creates a Reporting draft; governance and delivery states remain separate.
Each installment has its own notice action and retained status, including when one investor has
multiple commitments in the call. New requests explicitly select that line's commitment.
Each deliberate Generate/Regenerate notice action creates one operation UUID. An unconfirmed
request retains that exact body for retry, recovering the original Reporting run; a deliberate
regeneration creates a new operation and run. The shared mutation lock rejects duplicate clicks.
Before sending, the original operation and exact tenant/company/entity/fund/book/currency/call/
investor/commitment target are retained in this tab's session storage; no amounts or authority are stored.
Reloading the original tab offers Recover original notice without sending automatically. The
record clears only after the response identifies that operation's exact retained run. Missing
storage blocks sending, and malformed records are removed with an explicit review notice.
An earlier investor-level pending intent migrates only when that investor has one call line,
preserving its UUID and original omitted-selector request body. Multiple lines leave that legacy
intent untouched and explain that automatic recovery cannot select an installment.
Browser reload recovery lasts while the original tab session retains its intent; server restart
recovery requires that known operation ID and the authenticated scope to remain available.
Only an authorized terminal-failure response matching the original operation's exact run enables
Generate new notice after confirmed failure. The original intent stays retained until that
explicit action safely retains a new UUID; generic conflicts, denied access, in-flight and unknown
outcomes keep the original. Failure confirmation is not persisted: after reload, Recover original
notice must obtain a fresh server confirmation before a new operation is offered. Failed run
audit history remains on the server.

Changing book selection disables the previous scope until Apply. Scope/session changes discard
old results, input changes cancel pending previews, and unconfirmed writes retain the same request
for retry. Journal and report links open the existing review workflows. The seven root workspaces
remain unchanged. Focused coverage: `capital-call-issuance-screen.test.tsx`,
`private-capital-installment-notices.test.tsx`, `private-capital.api.test.ts`, and the existing pure
capital-call presentation tests.

### Posted-ledger account drill-down

Selecting a trial-balance account in the Accounting posted-ledger panel renders
`LedgerAccountRunningBalancePanel`, which lists the account's postings for the selected period
with a running balance from `GET /api/ledger/periods/{periodId}/accounts/running-balance`.
The panel sends the selected row's accounting basis, symbol, financial account and dimension set,
so the postings and ending balance are those of that row only. Basis and row changes discard the
previous scope's response. Missing book currency remains explicitly unavailable rather than
defaulting to dollars. Legacy API callers that omit accounting basis retain an unfiltered read. The
route helper lives in `src/lib/ledger-reports-api.ts` rather than `workstation-endpoints.ts`,
which is at its file-size limit.
