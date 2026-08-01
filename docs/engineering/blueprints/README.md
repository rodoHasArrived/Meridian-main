# Engineering Blueprints

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-01

Code-ready technical design documents produced by Blueprint Mode. Each blueprint translates a
prioritized idea into named interfaces, component designs, data flows, a test plan, and an
implementation checklist grounded in Meridian's actual stack.

## Index

- [Repo Engine, Depreciation Schedule, and Borrower-Side Debt](financing-liabilities-depreciation-blueprint.md)
  — three financing/accounting engines (repo/reverse-repo, fixed-asset depreciation, fund-as-borrower
  term debt) that plug into the existing projector → ledger → approval pipeline.
- [W10-MARK-001 — Fail-Closed Mark Freshness and Mark-Age Surfacing](w10-mark-001-fail-closed-marks.md)
  — consolidates the two half-used mark-freshness controls into one fail-closed policy, adds a scoped
  expiring price override, and surfaces mark age on both workstation lanes. Implements roadmap row
  `W10-MARK-001`, rank 1 of the 2026-07 depth slate.
