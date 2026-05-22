# Fund Operations Persistence Cutover Status
**Last Updated:** 2026-05-22

## Domains
- Fund Structure: Shadow-write scaffolded, reconciliation job registered, read cutover gated by config toggle.
- Fund Accounts: Shadow-write scaffolded, reconciliation job registered, read cutover gated by config toggle.
- Direct Lending: Shadow-write scaffolded, reconciliation job registered, read cutover gated by config toggle.
- Banking: Shadow-write scaffolded, reconciliation job registered, read cutover gated by config toggle.
- Money Market: Shadow-write scaffolded, reconciliation job registered, read cutover gated by config toggle.

## Current state
- Default read mode remains `LegacyInMemory` for safety.
- Reconciliation hosted service runs domain jobs and emits discrepancy warnings.
- Canonical projection schema records are defined for all five domains.
