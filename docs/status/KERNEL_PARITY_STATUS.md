# Kernel Parity Status

Last updated: 2026-04-20

This status page tracks C# ↔ F# migration parity coverage by subsystem. Coverage is defined as:

`covered fixture scenarios / required fixture scenarios`

Expected divergences are counted as covered only when an active annotation exists with a clear justification and non-expired `expiresOn` date.

## Coverage Table

| Subsystem | Required Scenarios | Covered Scenarios | Coverage % | Expected Divergences | Last Verified | Owner | Notes |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| Risk policy kernel | 0 | 0 | 0% | 0 | _pending_ | _unassigned_ | Bootstrap fixtures pending |
| Ledger reconciliation kernel | 0 | 0 | 0% | 0 | _pending_ | _unassigned_ | Bootstrap fixtures pending |
| Cash-flow projection kernel | 0 | 0 | 0% | 0 | _pending_ | _unassigned_ | Bootstrap fixtures pending |
| Security Master validation kernel | 0 | 0 | 0% | 0 | _pending_ | _unassigned_ | Bootstrap fixtures pending |

## Update Procedure

1. Run kernel parity suites in CI or locally.
2. Update scenario counts for each touched subsystem.
3. Recalculate `Coverage %` as integer percentage.
4. Increment/decrement `Expected Divergences` based on active annotations.
5. Stamp `Last Verified` with ISO date (`YYYY-MM-DD`).
6. Link related PR(s) in the Notes column for auditability.

## Guardrails

- Unannotated `score`, `severity`, or `reason` mismatches are release-blocking for kernel-related PRs.
- Divergence annotations must include business justification and expiration.
- Expired divergence annotations must fail parity checks until renewed or removed.
