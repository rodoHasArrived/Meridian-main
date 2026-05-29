# UFL Accounting Impact Model

**Owner:** Core Team
**Audience:** Accounting, ledger, reconciliation, reporting, application, and workstation contributors
**Last Updated:** 2026-05-28
**Status:** target-state shared model

## Summary

Accounting impact is a UFL capability, not a default requirement for every asset profile. Reference-heavy assets can mature through canonical terms, reference reads, and projection safety before they need journal workflows. Operationally heavy assets such as direct lending, repo, swaps, cash sweeps, and equity corporate actions need explicit accounting and reconciliation paths.

## Applicability

| Asset family | Default accounting posture |
| --- | --- |
| Reference-heavy assets | Defer accounting until lifecycle or corporate-action events require it. |
| Lifecycle/cashflow-heavy assets | Add accounting previews once cash-flow or accrual projections become stable. |
| Operationally heavy assets | Treat accounting/reconciliation as core target behavior. |
| Custom asset profiles | Allow accounting-impact hints only through approved capabilities; do not allow arbitrary journal logic. |

## Capability Boundary

AccountingImpact includes:

- deterministic accounting-impact preview from canonical terms and projections;
- draft journal generation with source event, command, security, fund, and approval metadata;
- approval and correction workflow before posting when material or ambiguous;
- reconciliation evidence linking source event, projection, journal, cash/position effect, and report line;
- period-control awareness so locked periods reject or queue postings.

AccountingImpact does not include:

- silent posting from raw provider payloads;
- pricing, valuation, or risk analytics unless a separate asset profile explicitly owns them;
- bypassing ledger validation or period controls;
- mobile-specific workflows.

## Evidence Boundary

### Implemented

- Meridian has ledger, accounting, reporting, reconciliation, and evidence surfaces that asset profiles can target.
- Direct lending documents and code evidence already describe deeper journal, reconciliation, and rebuild behavior than most reference assets.
- Equity target-state documentation includes corporate-action accounting automation as an explicit target capability.

### Partially Implemented

- Some assets expose canonical reference data that accounting can consume, but not asset-specific journal workflows.
- Reconciliation evidence exists in broader fund-ops workflows, but UFL per-asset conformance remains uneven.

### Target-State Only

- Shared AccountingImpact conformance checks for UFL assets.
- Asset-level accounting preview contracts outside direct lending and selected equity workflows.
- Common evidence packets tying UFL projections to journal and reconciliation artifacts.

### Explicitly Out of Scope

- Requiring every UFL asset to reach L5.
- Full pricing or risk methodology.
- Posting from provider-only records.

## Milestone Contract Pattern

For any asset pursuing L5:

**Goal:** Canonical projected events can produce controlled accounting previews, approved journal drafts, reconciliation links, and reporting evidence.

**Likely files:** ledger/application services, asset-specific DTOs, `src/Meridian.Ui.Shared/Endpoints/`, and focused tests under `tests/`.

**Acceptance evidence:** preview tests, journal-balance tests, period-control tests, reconciliation-link tests, and endpoint contract tests.

**Exit criteria:** no accounting claim is marked delivered without a named validating test or evidence packet.

## Related Documents

- [UFL Capability Model](ufl-capability-model.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
- [UFL Direct Lending Capability Profile](ufl-direct-lending-target-state-v2.md)
- [UFL Equity Capability Profile](ufl-equity-target-state-v2.md)
