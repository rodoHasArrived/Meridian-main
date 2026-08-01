# Engineering Blueprints

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-06

Code-ready technical design documents produced by Blueprint Mode. Each blueprint translates a
prioritized idea into named interfaces, component designs, data flows, a test plan, and an
implementation checklist grounded in Meridian's actual stack.

## Index

- [Repo Engine, Depreciation Schedule, and Borrower-Side Debt](financing-liabilities-depreciation-blueprint.md)
  — three financing/accounting engines (repo/reverse-repo, fixed-asset depreciation, fund-as-borrower
  term debt) that plug into the existing projector → ledger → approval pipeline.
- [Risk Engine: Severity-Aware Evaluation and Pre-Trade Decision Journal](risk-engine-severity-and-decision-journal-blueprint.md)
  — makes `IRiskRule.Severity` decisional, evaluates every rule instead of stopping at the first
  failure, returns structured violations, and journals pre-trade decisions through the existing
  WAL-backed execution audit trail. Engine prerequisite for the `W9-SAFETY-007` rule catalogue.
