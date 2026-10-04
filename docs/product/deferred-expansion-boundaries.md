---
doc_type: product-boundary
doc_schema: meridian.product-boundary
doc_schema_version: "1.0.0"
owner: core-team
status: active
last_reviewed: 2026-08-03
---

# Deferred Expansion Boundaries

This document records the narrow acceptance boundaries for product areas that remain deferred beyond the operational-record baseline. These boundaries are not implementation claims; they define the minimum evidence that must exist before a future roadmap row can move one of these areas into active build scope. A completed boundary item in the implementation tracker means the scope decision and reopening gate are explicit, not that the deferred product has shipped.

## Treasury Payments

Native live payment execution remains deferred. A future roadmap row must define payment request records, approval separation, bank release automation, payment-processor orchestration, expected cash movement, bank confirmation, return and reversal evidence, reconciliation linkage, and audit retention before any live payment release surface is implemented.

The existing non-executing evidence seam must retain regression tests for payment request creation, approval recording, expected cash movement projection, bank confirmation capture, return or reversal evidence, reconciliation handoff, and audit linkage. A future live-execution implementation must additionally introduce requester identity and prove requester/approver separation; the current pending-payment contract does not claim maker-checker enforcement.

## Alternative Asset Operations

Alternative asset operations now have a v1 minimum coverage baseline inside the existing Security Master passport/detail, Asset Operations, and `/api/workstation/portfolio/multi-asset-coverage` flows: `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`, and retained `DirectLoan` rows declare required terms, provider/source evidence, ledger classification, reconciliation signals, and close/reporting handoff. Broader expansion remains deferred for live eFront/Yardi/cap-table/trustee adapters, new root workspaces, or core-ledger rewrites until a future roadmap row defines provider integration, posting automation, governance, and acceptance evidence.

## Enterprise Risk

Enterprise risk remains a future program. A roadmap row must define stress and scenario inputs, independent risk cockpit responsibilities, cross-portfolio governance, breach acknowledgement and acceptance records, retained evidence, and escalation/audit behavior before building an enterprise risk surface.

## Forecasting

Forecasting remains deferred until a roadmap row defines the engine boundary, scenario inputs, retained forecast evidence, budget/cash/close/report linkages, operator acceptance criteria, and focused tests proving evidence retention and downstream handoff.

## Capital Structure Modeling

Capital-structure modeling must define debt and equity waterfalls, commitments, obligations, covenants, financing events, approval requirements, evidence retention, and downstream ledger/reporting handoff before it can be marked as implemented.

## Administration Dashboards

Broad fund, book, period, report, and delivery administration dashboards remain deferred beyond the existing Settings, policy, provider, approval, tenant-readiness, and audit surfaces. A future roadmap row must first establish durable tenant-scoped managed state, separation-of-duties and approval gates, effective-dated change history, rollback or correction behavior, operator recovery, and focused acceptance evidence over shared services and read models. In-memory control prototypes are not production administration authority.

## Reporting and Analytics Platform

The bounded reporting platform baseline is implemented through governed report-pack runs, certified datasets, line provenance, scheduling, approval, immutable artifacts, distribution evidence, amendments, and restatements. This does not close broader analytics-product expansion. Client-grade PDF/XLSX rendering and the partners-capital statement are delivered and accepted under `W9-REPORT-005` (operator decision `DEC-W9-ACCEPTANCE-001`, 2026-08-29), so that work is no longer deferred; what remains outside the boundary is self-service analytics beyond the governed reporting baseline. Any additional analytics workspace, dataset, semantic-model, or self-service distribution surface must reuse the canonical reporting run and evidence chain and receive its own roadmap acceptance criteria before implementation.

## Client Portal

Broad self-service client portal work remains deferred. The first acceptable slice must define entitlement checks, recipient approval, delivery evidence, request history, amendment and restatement handling, access revocation, and audit retention before exposing portal-style self-service workflows.

## No-Code Workflow Designer

No-code workflow design remains deferred until policy-safe configuration boundaries exist. A future row must define allowed workflow shapes, approval rules, versioning, test cases, activation controls, rollback behavior, and audit evidence before UI design or activation tooling proceeds.

## Document Vault

Evidence Vault productization is complete as the bounded browser-first `W5X-EVIDENCE-001` baseline,
with the statement reconciliation onboarding wedge completed in `W5X-STMT-ONBOARD-001`. That v1 slice
uses existing request-list, immutable-manifest, extracted-field review, document-to-object link,
retention, access-control, and audit-evidence primitives; its WPF UI parity is now tracked as
`W8-WPF-PARITY-001` (the Evidence Workbench is a Wave P1 parity item).
Broader document-portal, collaboration, and self-service document management expansion remains
deferred until a later roadmap row defines entitlement, assignment, request history, and audit
retention gates.

## Collaboration

Collaboration expansion beyond current workflow queue support must define operator comments, assignments, waiting-on-evidence state, waiting-on-approval state, escalation history, durable audit retention, and permission boundaries before it becomes a productized surface.

## Mobile

Native iOS/Android, MAUI, React Native, Flutter, and mobile-first workflows remain closed. Responsive browser validation is allowed for the browser workstation, but mobile application development requires an explicit roadmap reopening decision.
