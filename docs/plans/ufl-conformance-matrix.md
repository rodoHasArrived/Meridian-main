# UFL Conformance Matrix

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, application, and workstation contributors
**Last Updated:** 2026-05-28
**Status:** active planning matrix

## Summary

This matrix is the single planning view for current UFL maturity and next conformance targets. It is intentionally conservative: current levels must be backed by code and test evidence named in the asset package, while target additions remain target-state only until evidence is recorded.

Maturity levels are defined in [UFL Capability Model](ufl-capability-model.md).

## Matrix

| Asset | Current level | Next level | Missing capability | Evidence needed |
| --- | --- | --- | --- | --- |
| Direct Loan | L3/L4 partial | L5 | outbox workers, accounting/reconciliation hardening, operational close controls | direct-lending integration tests, journal/reconciliation tests, endpoint evidence |
| Equity | L1/L2 partial | L3 | lifecycle, alias, preferred/convertible, and corporate-action projections | endpoint tests, projection tests, corporate-action accounting preview tests |
| Option | L1/L2 partial | L3 | series, lifecycle, alias, and adjusted-contract projections | chain normalization tests, underlying-link tests, projection rebuild tests |
| Bond | L1/L2 partial | L3 | lifecycle, accrual, issuer, and maturity-ladder projections | fixed-income projection tests, rebuild/checkpoint tests, endpoint contract tests |
| Treasury Bill | L1 | L2/L3 | ladder, auction, lifecycle, and treasury reference endpoints | mapping tests, projection tests, endpoint contract tests |
| Future | L1 partial | L2 | contract-month reference reads and lifecycle projection | mapping tests, futures reference endpoint tests |
| FX Spot | L1/L2 partial | L3 | canonical alias projection and provider-independent pair reads | mapping tests, reference endpoint tests, rebuild metadata tests |
| Deposit | L1/L2 partial | L3 | institution, maturity, lifecycle, and accrual projections | deposit reference endpoint tests, projection tests |
| Money Market Fund | L3 partial | L4 | operator review of liquidity gates and rebuild evidence | rebuild checkpoint tests, workstation/control tests |
| Certificate of Deposit | L1/L2 partial | L3 | issuer, callable, maturity, and accrual projections | reference endpoint tests, projection rebuild tests |
| Commercial Paper | L1 | L2/L3 | issuer, maturity, discount, and lifecycle projections | mapping tests, fixed-income projection tests |
| Repo | L1 partial | L2 | agreement/reference/exposure reads and counterparty linkage | storage tests, endpoint tests, collateral/exposure tests |
| Cash Sweep | L1 partial | L2/L3 | sweep program reference reads and cash workflow projections | mapping tests, projection tests |
| Swap | L1 partial | L2/L3 | leg reference reads, counterparty linkage, collateral metadata | swap leg mapping tests, endpoint tests, projection tests |
| Commodity | L1 partial | L2 | commodity reference reads and provider alias isolation | mapping tests, endpoint tests |
| Crypto | L1/L2 partial | L3 | network/venue aliases, custody metadata, provider-independent projections | endpoint tests, alias projection tests |
| CFD | L1 partial | L2 | underlying/exposure reference reads and margin metadata | mapping tests, endpoint tests |
| Warrant | L1 partial | L2/L3 | underlying link, expiry, lifecycle, and conversion projections | mapping tests, underlying-link tests, projection tests |
| Other Security | L1 | L2/L3 | review, taxonomy, custom-profile handoff, and promotion projections | category validation tests, governance endpoint tests, projection tests |
| Custom Asset Profile | L0 target-state | L1 | governed profile definitions, typed fields, validation, and version pinning | profile validation tests, Security Master reference tests |

## Planning Rules

- `L1` requires canonical terms and validation, not only a roadmap entry.
- `L2` requires stable read contracts or endpoints over canonical reference data.
- `L3` requires replay-safe projection metadata, checkpointing, and rebuild tests.
- `L4` requires operator workflow, approval/correction controls, and audit evidence.
- `L5` requires accounting, reconciliation, period/reporting evidence, and tests.
- If evidence is mixed, use `partial` instead of rounding up.

## Related Documents

- [UFL Supported Asset Packages](ufl-supported-assets-index.md)
- [UFL Capability Model](ufl-capability-model.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
- [UFL Custom Asset Composability](ufl-custom-asset-composability.md)
