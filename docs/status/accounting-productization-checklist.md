# Meridian Accounting Productization Checklist

Last updated: 2026-06-23

This checklist tracks progress toward production-grade, configurable, multi-ledger accounting. It is
not a release certification; items stay open until current source, tests, and operator surfaces prove
the requirement end to end.

## Completed Or Substantially Implemented

- [x] Ledger-book identifiers are carried through core accounting contracts, configuration
  workspaces, journal drafts, posting candidates, reports, and external GL export packages.
- [x] Accounting configuration can save ledger-book-scoped chart nodes, templates, posting rules,
  saved rule tests, audit events, and activation state.
- [x] Rules Studio supports effective-dated rules, priority, predicates, formulas, allocations,
  generated posting lines, dry-run previews, saved regression tests, version history, and promotion
  approval metadata in shared DTOs/services.
- [x] Browser Accounting Rules Studio uses shared endpoints for dry-runs, saved tests, rule edits,
  generated-posting capture, promotion approvals, and workspace refresh.
- [x] Accounting Configure now receives a server-derived ledger-book setup candidate when a
  book-scoped workspace references a missing book but the fund has registered ledger-book scope,
  and the browser setup surface can create the ledger book through the shared ledger-book endpoint
  instead of guessing fund-structure node context locally. Browser Accounting Configure also renders
  a shared-workspace ledger-book administration catalog with selected/available books, fund/entity
  scope, basis, currency, accounting policy, description, and update timestamps.
- [x] WPF Accounting Configure loads the active ledger-book workspace before rendering accounting
  configuration and now runs saved Rules Studio test suites plus promotion approvals through the
  shared accounting configuration service; desktop setup-readiness rows surface the shared
  ledger-book setup action guidance and the WPF action can create the ledger book through the
  shared ledger-book service. The desktop Configuration tab now also renders the shared-workspace
  ledger-book administration catalog with selected/available books, fund-structure scope, basis,
  currency, accounting policy, description, and update timestamps.
- [x] Posting-rule dry-runs can produce ledger-book-scoped governed journal draft candidates with
  generated multi-line postings, dimensions, evidence links, and approval-gated posting commands;
  source-event candidates without a ledger book fail closed before draft/write creation.
- [x] Manual journal drafts carry dimensions, evidence, ledger-book scope, lifecycle transitions,
  reversal/rebook links, and validation issues; save, validate, submit, evidence attachment, and
  lifecycle mutation requests reject a requested ledger book that does not match the retained draft.
- [x] External GL remains guarded and import-first: QuickBooks fixture/import evidence,
  Xero and NetSuite fixture import evidence, provider-neutral mapping profiles,
  ledger-book-scoped reconciliation, and controlled export package certification exist while live
  external posting stays disabled. Provider rows now expose QBO/Xero/NetSuite-specific mapping
  requirements for account mapping, journal lineage, trial-balance tie-out, and dimension mapping,
  and browser/WPF Accounting Configure surfaces consume those shared setup requirements.
- [x] Close/report package and report-line DTOs carry ledger-book, reconciliation, certification,
  restatement, and evidence state for downstream close/reporting surfaces.
- [x] Strategy-run workstation ledger trial-balance and journal reads now carry canonical
  `LedgerDimensionSetDto` scope and fail-closed filters for fund/entity/sleeve/strategy/portfolio/
  account plus external GL dimensions from source run parameters.
- [x] Direct-lending ledger-impacting event projections stamp generated journal lines with
  ledger-book, borrower legal-entity/counterparty, Security Master instrument, and loan account
  dimensions before handing the posting candidate to durable journal storage.
- [x] Operations Continuity posting candidates can carry shared `LedgerDimensionSetDto` on each
  journal line, and the Financial Operations posting gate maps those dimensions into immutable
  ledger lines before appending durable close/reconciliation/accrual journals.
- [x] Financial Operations accounting-close trial-balance projection now preserves line dimensions,
  buckets same-account activity by dimensional scope, and supports scoped close/report filters over
  the canonical dimension set plus external-GL dimensions.
- [x] Durable ledger journal storage now canonicalizes first-class line dimensions before JSONB
  persistence, query containment, and rehydration, covering fund, entity, sleeve, strategy,
  investor, capital account, instrument, tax lot, cost center, counterparty, external GL, and
  customer-neutral accounting scopes.
- [x] Ledger-owned report-pack and scheduled-export dimension filters now share canonical
  line-dimension matching/formatting, so messy or duplicate external-GL dimension inputs do not
  split report scopes from durable journal query behavior.
- [x] Shared ledger report endpoints now canonicalize dimension query filters, retained row
  dimensions, matching, and report signatures before browser/WPF clients render or certify
  scoped trial-balance reports.
- [x] Browser Accounting ledger inquiry now mirrors the shared trial-balance dimension contract,
  renders dimension summaries and detail fields, and lets GL account searches match retained fund,
  entity, sleeve, strategy, investor, capital account, instrument, tax-lot, cost-center,
  counterparty, and external-GL dimensional scope instead of hiding scoped rows behind account names.
- [x] Browser retained ledger journal contracts now mirror the shared journal dimension fields, and
  the Accounting view model has a reusable journal-evidence dimension projection/filter for rows
  returned by the shared run ledger journal endpoint.
- [x] Browser run-ledger API helpers now accept canonical dimension-scoped query options for
  trial-balance and retained journal reads, including fund, entity, sleeve, strategy, investor,
  capital account, instrument, tax lot, cost center, counterparty, and external GL dimension keys,
  so workstreams can request server-filtered ledger evidence instead of relying only on
  client-side display/search.
- [x] WPF Run Ledger dense tables and selected-line inspector now project canonical
  `LedgerDimensionSetDto` scope from shared trial-balance and journal rows, including fund, entity,
  sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty,
  account/portfolio scope, and external GL dimensions, with legacy scope labels used only when
  canonical dimensions are absent.
- [x] Cross-period ledger trial-balance and P&L report endpoints now fail closed when retained
  closed-period summary metadata belongs to a different ledger book than the selected period,
  preventing stale summary drift from leaking totals across parallel books.
- [x] Ledger period close summaries now count open reconciliation breaks only when the operator
  work item carries explicit ledger-book or ledger-period scope, preventing unrelated accounting
  inbox breaks from leaking across parallel books during close.
- [x] Shared reconciliation break queue items now carry optional ledger-book scope, and
  ledger-book-scoped Accounting/Reconciliation workstation payloads filter break queues,
  calibration summaries, open-break metrics, and control-center state to explicit book cases while
  excluding unscoped legacy queue items.
- [x] Shared accounting production-readiness assessment now aggregates ledger-book rollout,
  Rules Studio, posting-rule execution, dimensional coverage, external-GL mapping/posting posture,
  journal lifecycle service registration, close/report service registration, migration-rollout
  certification blockers, and tenant-admin rollout blockers into one read-only fail-closed contract
  and endpoint for browser, WPF, and admin setup surfaces.
  Migration readiness now accepts retained evidence flags for ledger-book migration, historical
  journal backfill, dimensional backfill, configuration promotion, and close/reporting evidence
  migration plus retained migration run artifacts, and returns blocking shared issue codes when
  controls are not certified, certified controls lack retained certified run artifacts, or retained
  run artifacts failed. Retained migration run artifacts now have a shared file-backed store and
  Accounting System list/upsert endpoints, and production readiness automatically merges stored
  fund/book-scoped artifacts into the assessment. The shared production-readiness payload now also
  emits a migration rollout plan for ledger-book scope, historical journal backfill, dimensional
  backfill, configuration promotion, and close/reporting evidence migration, including lane status,
  scope, latest retained run, migrated-record and issue counts, blocking issue codes, and required
  actions. Browser Accounting Configure now reads the retained artifact list endpoint and renders
  both the rollout plan and run kind, status, scope, migrated-record count, issue count, and
  evidence-reference count beside production-readiness blockers. WPF Accounting Configure renders
  the same shared migration rollout plan and retained run evidence. Migration rollout readiness now
  also fails closed when the assessment lacks tenant/company scope or when retained migration run
  artifacts belong to another tenant/company rollout. Accounting System now exposes a governed
  migration-run execution endpoint that stamps authenticated tenant/company/actor scope, rejects
  assistant or automation-origin runs, creates scoped retained run artifacts, and feeds the same
  production-readiness rollout plan. Migration execution now also accepts source-store and
  migrated-row counts on retained runs, blocks incomplete, negative, or mismatched supplied counts,
  and retains successful row-count reconciliation as artifact metadata plus evidence tokens.
- [x] Source-event posting candidates and production-readiness Rules Studio checks now use
  tenant/company/fund/ledger-book scope for dry-run, workspace lookup, chart resolution, and
  browser/WPF endpoint entry, preventing a candidate or readiness assessment for one company from
  falling back to another company's retained Rules Studio workspace.
- [x] Ledger-book-native production certification is now evidence-qualified by workflow lane:
  posting rules, journal lifecycle, close/reporting, external GL, reconciliation, direct-lending
  projections, and strategy ledger reads require retained evidence for that lane, or an explicit
  full workflow certification packet, before readiness counts the control as complete. Browser and
  WPF production-certification editors can retain the added reconciliation, direct-lending, and
  strategy-ledger-read controls under the shared Accounting System profile store.
- [x] Dimensional reporting/export production certification is now evidence-qualified by lane:
  posted ledger-line dimensions, trial-balance filters, period reports, cross-period reports,
  journal filters, report-package provenance, and external export mappings require retained
  evidence for that lane, or an explicit full dimensional/production certification packet, before
  readiness counts the control as complete.
- [x] Close/Reporting production readiness now consumes the dimensional reporting/export
  certification state directly, so report package readiness remains blocked until posted
  ledger-line dimensions, trial-balance filters, period reports, cross-period reports, journal
  dimension filters, report-package provenance, and external-export dimension mappings have retained
  ledger-book-scoped evidence.
- [x] Tenant administration production readiness is now evidence-qualified by setup lane: tenant
  scope, admin roles, scoped access, reporting groups, aggregate operator surface, browser admin
  studio, and WPF admin studio each require retained evidence for that lane, or an explicit
  setup-certified tenant-admin packet, before readiness counts the control as complete.
- [x] Posting Rule Execution, Journal Lifecycle, Close/Reporting, External GL, reconciliation,
  direct-lending, and strategy ledger-read
  production-readiness components now consume ledger-book-native workflow certification and
  retained lane evidence directly, so those components remain blocked even when generated rules or
  services are present until the selected ledger book has posting-candidate, lifecycle,
  close/reporting, import, reconciliation, mapping, guarded-export, direct-lending projection, and
  strategy-run ledger-read proof.
- [x] Tenant administration readiness now includes explicit enterprise configuration studio
  controls for chart administration, rule-test/promotion setup, close setup, provider/external-GL
  mapping setup, tenant/company/report-group setup, ledger-book administration, posting-rule
  authoring, approval queues, dimension mapping, and implementation sandbox validation. The shared
  profile, browser editor, WPF request/profile path, production-readiness blockers, and retained
  evidence checks all use the same control-plane model.
- [x] Tenant administration readiness now also exposes operational-hardening controls for audit
  review tooling, bulk import/export safeguards, performance validation, and disaster-recovery
  runbooks. Browser and WPF setup surfaces carry the same shared profile fields, and production
  readiness blocks until those controls have retained tenant-admin evidence or a full setup packet.
- [x] Retained accounting production certification and migration-run artifacts now reject extended
  ledger-book evidence tokens such as `ledger-book:{id}ffff`, so durable readiness stores cannot
  certify one book by matching a longer unrelated identifier prefix.
- [x] Accounting report package certification now applies the same exact-token ledger-book evidence
  check, preventing certified financial statement, NAV, restatement, and export artifacts from
  accepting `book:{id}ffff` as selected-book provenance. Certification evidence also requires
  exact-token package id, certification id, period, tenant/company scope, and dimension-scope
  provenance before reports, NAV, restatement, or export artifacts can be certified.
- [x] Close-management task sign-off and late-adjustment request/review evidence now apply
  exact-token ledger-book, close-task, journal-entry, and late-adjustment request evidence checks,
  preventing extended identifiers such as `book:{id}ffff` from satisfying selected close-control
  provenance.
- [x] Close-management task sign-off now also enforces reviewer independence from the actor who
  acknowledged/prepared the checklist task before retained sign-off evidence can satisfy the close
  matrix.
- [x] Rejected close-management task sign-off decisions now block the close task, close-calendar
  milestone, and close-plan validation until the failed retained control is remediated.
- [x] Close-management sign-off retention now also rejects any later same-role decision while a
  retained rejection is active, so a second approval cannot overwrite a failed control without a
  remediation workflow.
- [x] Close-management dependency projection now requires predecessor tasks to be signed off before
  dependent tasks advance, emits close-plan validation issues for unresolved dependencies, and
  proves both waiting and advanced dependency states in focused Financial Operations tests.
- [x] External GL export-control and export-certification evidence recognizes colon-delimited
  ledger-book scope as a valid exact-token boundary while still rejecting extended ledger-book
  identifiers.
- [x] External GL mapping-profile certification evidence now requires exact-token profile,
  provider, and fund provenance, so extended profile identifiers cannot certify guarded export
  mappings by prefix.
- [x] Production-readiness dimensional rollout evidence now requires exact-token tenant, company,
  fund, and ledger-book provenance before retained dimension/report/export certification can count
  for the selected rollout scope.
- [x] Tenant administration production-readiness evidence now requires exact-token tenant and
  company provenance, so extended tenant or company identifiers cannot certify enterprise setup
  controls by prefix.
- [x] Tenant administration production-readiness now also requires selected ledger-book evidence for
  operational-hardening controls: audit review tooling, bulk import/export safeguards, performance
  validation, and disaster-recovery runbooks.
- [x] Accounting migration-run execution now fails certification requests unless operator-retained
  evidence names the selected tenant, company, fund profile, ledger book, and migration kind.
- [x] Close-period plans now emit critical validation blockers for every unsatisfied required
  close-task sign-off role, even when the upstream workflow checklist marks the task done, so close
  cockpit, report certification, and period-lock surfaces see missing approvals before package
  assembly.
- [x] External GL export certification and export-control provenance now require exact-token
  package, certification, fund/provider, and period evidence instead of accepting longer identifier
  prefixes.
- [x] Manual journal lifecycle approval, posting, close-lock, reversal, and rebook evidence now
  requires exact-token journal-entry, period, ledger-book, tenant, and company provenance, so
  extended lifecycle identifiers cannot satisfy governed transition evidence by prefix.
- [x] Manual journal draft, submit, evidence-attach, and lifecycle endpoints now stamp trusted
  tenant/company, actor, and report-group principal context onto shared-service requests, so manual
  journal audit events retain browser/WPF session role/profile scope instead of relying on
  body-supplied authorization metadata.
- [x] Manual journal save/validate/submit end-to-end workstation proof now runs under an
  authenticated tenant/company session, so Accounting and Reporting workspace projections are
  tested through the same tenant-scope gate used by production workstation routes.
- [x] Durable PostgreSQL journal reads now apply account and line-dimension filters through a
  matching-entry subquery before rehydrating all retained journal legs, preventing scoped ledger
  queries from returning unbalanced partial entries to close, reporting, reconciliation, or export
  consumers.
- [x] Report package restatement workflows now require retained lineage evidence that names the
  exact prior package or certification id being restated, so generic prior-period support cannot
  move a restatement package to certification review.
- [x] Final restatement package certification now also requires the retained approval artifact to
  name the exact prior package being restated before approving the statement and NAV restatement
  workflow metadata.
- [x] Close task sign-off evidence now boundary-checks role and period tokens, so extended role or
  period identifiers cannot satisfy retained close approval provenance by prefix.
- [x] Late-adjustment request and review evidence now boundary-checks close-period tokens, blocking
  extended period identifiers from requesting or approving material close adjustments.
- [x] Material late-adjustment review now enforces requester/reviewer independence at the
  Financial Operations service and workstation endpoint layer before retaining an approval or
  rejection decision.
- [x] Pending material late adjustments now emit critical close-plan validation blockers until a
  controller review approves or rejects the retained request, so period-lock and report
  certification surfaces cannot treat unresolved late adjustments as advisory warnings.
- [x] Accounting report package certification now enforces certifier independence from the
  retained package preparer after evidence, close-plan, state, and critical-issue checks pass.
- [x] Accounting report package assembly now treats a missing retained report evidence package as a
  critical blocker and carries that blocker into the report-evidence readiness row, so financial
  statements, NAV, restatement, and export packages cannot appear ready for review without retained
  ledger, reconciliation, rendered-report, and NAV support evidence.
- [x] Standalone accounting report packages now carry wrong-ledger-book retained evidence blockers
  into the report-evidence readiness row, so operator review surfaces do not hide critical
  ledger-book evidence scope drift behind package-level validation only.
- [x] Accounting report package assembly now emits a dedicated report-dimension-scope readiness row
  for fund, ledger-book, investor, capital-account, and explicit dimension mismatches, so
  dimensional reporting blockers are visible to operator review surfaces before certification.
- [x] Accounting report export readiness now fails closed when retained export artifacts are missing
  evidence, content hashes, ledger-book alignment, or package dimension-scope alignment, so report
  export certification surfaces cannot treat artifact retention as complete from certification state
  alone.
- [x] Tenant administration production readiness now requires aggregate accounting admin, browser
  admin-studio, and WPF admin-studio evidence to name the selected ledger book, so enterprise setup
  surfaces cannot be certified from generic tenant/company evidence alone.
- [x] Tenant/company/report-group setup production readiness now also requires retained evidence for
  the selected ledger book, blocking generic enterprise setup packets from certifying multi-ledger
  reporting administration controls without book-specific proof.

## Still To Complete

- [ ] Prove every accounting workflow is ledger-book-native end to end, including close, reporting,
  reconciliation, external GL, direct-lending projections, strategy ledger reads, and any remaining
  fund-level compatibility paths.
- [ ] Turn Accounting Configure into a complete enterprise configuration studio: the shared
  certification model now separately tracks ledger-book administration, chart administration,
  posting-rule authoring, rule-test management, approval queues, dimension mapping, close setup,
  provider mapping setup, tenant/company/report-group controls, and implementation sandbox proof
  from both browser and WPF, but the underlying authoring/editing workflows still need deeper
  production-grade UX and execution coverage.
- [ ] Complete durable dimensional ledger persistence and query coverage for fund, entity, sleeve,
  strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external
  GL dimensions across all journal lines, report filters, close checks, and export mappings; close
  trial-balance projection, browser ledger inquiry, and browser retained-journal evidence rows now
  have dimension bucketing/filter/display proof, and browser run-ledger helpers can now request
  server-scoped dimension filters for trial-balance and journal reads. WPF Run Ledger also projects
  canonical dimensions from retained run ledger rows. PostgreSQL journal storage now preserves full
  balanced entries for account and line-dimension scoped reads, but not every durable query and
  report surface is covered.
- [ ] Finish JE lifecycle hardening across every mutation path: version guards, actor/segregation
  checks, evidence requirements, period locks, immutable posted entries, reversal/rebook correction
  paths, lifecycle idempotency, and transition audit coverage. Manual journal lifecycle commands now
  replay retained correlation ids for submit, approve, reject, post, close-lock, reverse, and rebook
  without appending duplicate transitions or audit events, approval/rejection/posting/close-lock/
  correction actions require an actor independent from the draft preparer, and endpoint-driven
  manual journal lifecycle audit now retains trusted role/profile principal scope. The primary
  manual journal workstation proof also exercises Accounting and Reporting projections with an
  authenticated tenant/company session. Broader authorization policy, service-level role
  enforcement, and end-to-end mutation coverage remain open.
- [ ] Add implementation-grade migration and rollout tooling for ledger-book scoping, historical
  journal backfill, dimensional backfill, accounting configuration promotion, and close/reporting
  evidence migration. The shared readiness contract now exposes fail-closed certification inputs,
  tenant/company-scoped retained migration run artifacts, retained-artifact blockers, and a shared
  migration rollout plan/checklist for these controls. A governed Accounting System migration-run
  endpoint now executes scoped rollout runs into retained artifacts and blocks automation-origin or
  unscoped runs, and supplied source-store versus migrated row-count mismatches now fail closed, but
  full historical journal/dimensional data rewrite workers and automated source-store count
  extraction remain open.
- [ ] Expand external GL provider depth beyond fixtures: Xero and NetSuite now have read-only
  import fixtures, provider-specific mapping requirements, and guarded export fixture coverage, but
  live credentialed import adapters, richer provider-specific mapping fixtures, and provider-owned
  controlled export certification still need coverage before any separately approved live posting
  adapter is considered.
- [ ] Productize close management with close-plan editing, dependency graph authoring, sign-off
  matrix administration, materiality policy setup, late-adjustment workflow, period locks, and
  blocker/evidence review in shared services plus browser/WPF surfaces. Backend close-plan
  projection now proves dependency-gated task progression, fail-closed missing sign-off blockers,
  and critical pending material late-adjustment blockers, but operator setup/editing workflows
  remain open.
- [ ] Productize reporting with financial statements, investor capital statements, realized
  gain/loss, NAV packages, report-line provenance, restatement workflows, certification states, and
  export evidence across all relevant ledger-book and dimension scopes. Package assembly now fails
  closed when retained report evidence is missing or points to the wrong ledger book, but broader
  report authoring, review, and operating workflows remain open.
- [ ] Add broader operational hardening: deeper admin UX, tenant-level setup workflows,
  authorization and report-group scoping, audit review workflows, bulk import/export execution
  safeguards, performance test automation, and disaster-recovery validation. The shared
  production-readiness endpoint now exposes explicit blocker controls for these lanes, but full
  browser/WPF workflow depth and operating runbooks are still open.
