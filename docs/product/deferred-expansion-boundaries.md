---
doc_type: product-boundary
doc_schema: meridian.product-boundary
doc_schema_version: "1.0.0"
owner: core-team
status: active
last_reviewed: 2026-06-23
---

# Deferred Expansion Boundaries

This document records the narrow acceptance boundaries for product areas that remain deferred beyond the W1-W7 operational-record baseline. These boundaries are not implementation claims; they define the minimum evidence that must exist before a future roadmap row can move one of these areas into active build scope.

## Treasury Payments

Native live payment execution remains deferred. A future roadmap row must define payment request records, approval separation, bank release automation, payment-processor orchestration, expected cash movement, bank confirmation, return and reversal evidence, reconciliation linkage, and audit retention before any live payment release surface is implemented.

Minimum tests: payment request creation, independent approval, expected cash movement projection, bank confirmation capture, return or reversal evidence, reconciliation handoff, and audit linkage.

## Alternative Asset Operations

Alternative asset expansion beyond the current structured and private-capital coverage must name the minimum asset classes, provider or administrator source evidence, Security Master identity requirements, valuation evidence, cash-flow evidence, ledger-classification requirements, and close/reporting handoff before the lane is treated as productized.

## Enterprise Risk

Enterprise risk remains a future program. A roadmap row must define stress and scenario inputs, independent risk cockpit responsibilities, cross-portfolio governance, breach acknowledgement and acceptance records, retained evidence, and escalation/audit behavior before building an enterprise risk surface.

## Forecasting

Forecasting remains deferred until a roadmap row defines the engine boundary, scenario inputs, retained forecast evidence, budget/cash/close/report linkages, operator acceptance criteria, and focused tests proving evidence retention and downstream handoff.

## Capital Structure Modeling

Capital-structure modeling must define debt and equity waterfalls, commitments, obligations, covenants, financing events, approval requirements, evidence retention, and downstream ledger/reporting handoff before it can be marked as implemented.

## Client Portal

Broad self-service client portal work remains deferred. The first acceptable slice must define entitlement checks, recipient approval, delivery evidence, request history, amendment and restatement handling, access revocation, and audit retention before exposing portal-style self-service workflows.

## No-Code Workflow Designer

No-code workflow design remains deferred until policy-safe configuration boundaries exist. A future row must define allowed workflow shapes, approval rules, versioning, test cases, activation controls, rollback behavior, and audit evidence before UI design or activation tooling proceeds.

## Document Vault

Document-vault productization must define request lists, immutable manifests, extracted-field review, document-to-object links, retention policy, access controls, and audit evidence before it can move from deferred scope to active implementation.

## Collaboration

Collaboration expansion beyond current workflow queue support must define operator comments, assignments, waiting-on-evidence state, waiting-on-approval state, escalation history, durable audit retention, and permission boundaries before it becomes a productized surface.

## Mobile

Native iOS/Android, MAUI, React Native, Flutter, and mobile-first workflows remain closed. Responsive browser validation is allowed for the browser workstation, but mobile application development requires an explicit roadmap reopening decision.
