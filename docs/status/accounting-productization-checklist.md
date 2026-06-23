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
  external posting stays disabled.
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
  artifacts belong to another tenant/company rollout.
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
  canonical dimensions from retained run ledger rows, but not every durable query and report surface
  is covered.
- [ ] Finish JE lifecycle hardening across every mutation path: version guards, actor/segregation
  checks, evidence requirements, period locks, immutable posted entries, reversal/rebook correction
  paths, lifecycle idempotency, and transition audit coverage. Manual journal lifecycle commands now
  replay retained correlation ids for submit, approve, reject, post, close-lock, reverse, and rebook
  without appending duplicate transitions or audit events, and approval/rejection/posting/close-lock/
  correction actions require an actor independent from the draft preparer. Broader authorization
  policy, role-based segregation, and end-to-end mutation coverage remain open.
- [ ] Add implementation-grade migration and rollout tooling for ledger-book scoping, historical
  journal backfill, dimensional backfill, accounting configuration promotion, and close/reporting
  evidence migration. The shared readiness contract now exposes fail-closed certification inputs,
  tenant/company-scoped retained migration run artifacts, retained-artifact blockers, and a shared
  migration rollout plan/checklist for these controls, but it does not yet execute migration jobs
  or backfill data.
- [ ] Expand external GL provider depth beyond fixtures: Xero and NetSuite now have read-only
  import fixtures, but live credentialed import adapters, richer mapping fixtures, and controlled
  export certification still need provider-specific coverage before any separately approved live
  posting adapter is considered.
- [ ] Productize close management with close-plan editing, dependency graph, sign-off matrix,
  materiality policy setup, late-adjustment workflow, period locks, and blocker/evidence review in
  shared services plus browser/WPF surfaces.
- [ ] Productize reporting with financial statements, investor capital statements, realized
  gain/loss, NAV packages, report-line provenance, restatement workflows, certification states, and
  export evidence across all relevant ledger-book and dimension scopes.
- [ ] Add broader operational hardening: deeper admin UX, tenant-level setup workflows,
  authorization and report-group scoping, audit review workflows, bulk import/export execution
  safeguards, performance test automation, and disaster-recovery validation. The shared
  production-readiness endpoint now exposes explicit blocker controls for these lanes, but full
  browser/WPF workflow depth and operating runbooks are still open.
