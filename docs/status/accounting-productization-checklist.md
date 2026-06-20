# Meridian Accounting Productization Checklist

Last updated: 2026-06-20

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
  instead of guessing fund-structure node context locally.
- [x] WPF Accounting Configure loads the active ledger-book workspace before rendering accounting
  configuration and now runs saved Rules Studio test suites plus promotion approvals through the
  shared accounting configuration service; desktop setup-readiness rows surface the shared
  ledger-book setup action guidance and the WPF action can create the ledger book through the
  shared ledger-book service.
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
  migration, and returns blocking shared issue codes when those controls are not certified.

## Still To Complete

- [ ] Prove every accounting workflow is ledger-book-native end to end, including close, reporting,
  reconciliation, external GL, direct-lending projections, strategy ledger reads, and any remaining
  fund-level compatibility paths.
- [ ] Turn Accounting Configure into a complete enterprise configuration studio: richer ledger-book
  editing/selection, chart administration, rule authoring, rule test management, approval queues,
  close setup, provider mapping setup, and tenant/company/report-group controls from both browser
  and WPF.
- [ ] Complete durable dimensional ledger persistence and query coverage for fund, entity, sleeve,
  strategy, investor, capital account, instrument, tax lot, cost center, counterparty, and external
  GL dimensions across all journal lines, report filters, close checks, and export mappings; close
  trial-balance projection now has dimension bucketing/filter proof, but not every durable query
  and report surface is covered.
- [ ] Finish JE lifecycle hardening across every mutation path: version guards, actor/segregation
  checks, evidence requirements, period locks, immutable posted entries, reversal/rebook correction
  paths, lifecycle idempotency, and transition audit coverage. Manual journal lifecycle commands now
  replay retained correlation ids for submit, approve, reject, post, close-lock, reverse, and rebook
  without appending duplicate transitions or audit events, and approval/rejection/posting/close-lock/
  correction actions require an actor independent from the draft preparer. Broader authorization
  policy, role-based segregation, and end-to-end mutation coverage remain open.
- [ ] Add implementation-grade migration and rollout tooling for ledger-book scoping, historical
  journal backfill, dimensional backfill, accounting configuration promotion, and close/reporting
  evidence migration. The shared readiness contract now exposes fail-closed certification inputs and
  blocker codes for these controls, but it does not yet execute migration jobs, backfill data, or
  retain migration run artifacts.
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
- [ ] Add broader operational hardening: admin UX, tenant-level setup workflows, authorization and
  report-group scoping, audit review tooling, bulk import/export safeguards, performance tests, and
  disaster-recovery validation. The shared production-readiness endpoint now exposes the blocker
  contract for these lanes, but full browser/WPF admin setup workflows and operating runbooks are
  still open.
