# Wave 4 Evidence Template

**Owner:** Governance and Ledger  
**Last Updated:** 2026-04-21  
**Status:** Active

This template standardizes Wave 4 evidence capture so each scenario can be replayed and audited without relying on implicit context.

## Required Evidence Fields

Every Wave 4 evidence record must provide all fields below:

1. **Deterministic scenario name** (`wave4-<domain>-<behavior>-<version>`)
2. **Input fixtures/data window** (fixture IDs, source files, and explicit UTC date/time window)
3. **Expected API assertions** (endpoint + deterministic assertion set)
4. **Expected workstation assertions** (screen/workflow + deterministic assertion set)
5. **Produced artifact location** (repo-relative artifact path)
6. **Regression owner** (team lane + individual owner)

## Record Template

Copy this block for each Wave 4 scenario:

```md
### Scenario: <deterministic-scenario-name>

- **Input fixtures/data window:**
  - Fixture set: `<fixture-id(s)>`
  - Window: `<start-utc>` to `<end-utc>`
  - Notes: `<normalization, seed rules, replay mode>`
- **Expected API assertions:**
  - `<method> <route>` -> `<assertion>`
  - `<method> <route>` -> `<assertion>`
- **Expected workstation assertions:**
  - `<workspace/page/component>` -> `<assertion>`
  - `<workflow step>` -> `<assertion>`
- **Produced artifact location:** `<repo-relative-path>`
- **Regression owner:** `<team lane> / <named owner>`
```

## Applied Scenarios

### Scenario: wave4-governance-identifier-conflict-resolution-v1

- **Input fixtures/data window:**
  - Fixture set: `fixtures/wave4/identifier-conflicts/conflict-chain-v1.json`, `fixtures/wave4/security-master/symbol-map-v3.json`
  - Window: `2026-03-03T00:00:00Z` to `2026-03-03T23:59:59Z`
  - Notes: deterministic seed `wave4-id-conflict-v1`; replay mode `single-pass`.
- **Expected API assertions:**
  - `POST /api/workstation/governance/reconciliation/resolve-conflict` -> returns stable conflict ID, selected canonical identifier, and `resolutionState=Resolved`.
  - `GET /api/workstation/governance/reconciliation/breaks?classification=IdentifierConflict` -> excludes resolved break and preserves audit trail reference.
- **Expected workstation assertions:**
  - `Governance > Reconciliation > Identifier Conflicts` queue shows one fewer pending conflict and displays canonical identifier in history row.
  - Conflict detail drawer shows immutable source identifiers, chosen canonical ID, resolver, and resolution timestamp.
- **Produced artifact location:** `artifacts/wave4/evidence/wave4-governance-identifier-conflict-resolution-v1/`
- **Regression owner:** `Governance and Ledger / Reconciliation On-Call`

### Scenario: wave4-governance-corporate-action-propagation-impact-v1

- **Input fixtures/data window:**
  - Fixture set: `fixtures/wave4/corporate-actions/split-dividend-chain-v1.json`, `fixtures/wave4/ledger/pre-action-balances-v2.json`
  - Window: `2026-02-17T00:00:00Z` to `2026-02-21T23:59:59Z`
  - Notes: includes one forward split and one cash dividend for same issuer lineage.
- **Expected API assertions:**
  - `POST /api/workstation/governance/corporate-actions/replay` -> reports applied actions count, impacted ledgers count, and deterministic replay checksum.
  - `GET /api/workstation/fund-operations/portfolio-impact?asOf=2026-02-21` -> position quantity, cost basis, and cash-flow deltas match fixture expectations.
- **Expected workstation assertions:**
  - `Governance > Fund Operations` impact panel surfaces action lineage and before/after portfolio posture for each affected sleeve.
  - `Governance > Reports` preview shows propagated corporate-action footnotes with matching reconciliation references.
- **Produced artifact location:** `artifacts/wave4/evidence/wave4-governance-corporate-action-propagation-impact-v1/`
- **Regression owner:** `Governance and Ledger / Fund Ops Productization`

### Scenario: wave4-governance-multi-ledger-reconciliation-break-classification-v1

- **Input fixtures/data window:**
  - Fixture set: `fixtures/wave4/reconciliation/multi-ledger-breaks-v1.json`, `fixtures/wave4/brokerage/sync-snapshots-v2.json`
  - Window: `2026-03-10T00:00:00Z` to `2026-03-12T23:59:59Z`
  - Notes: deterministic ordering by `ledgerId, breakId`; includes cash, position, and timing drift break types.
- **Expected API assertions:**
  - `POST /api/workstation/governance/reconciliation/classify-breaks` -> each break gets exactly one classification and severity according to deterministic classification ruleset `v1`.
  - `GET /api/workstation/governance/reconciliation/summary?scope=multi-ledger` -> summary totals equal per-break classifications and unresolved SLA counters.
- **Expected workstation assertions:**
  - `Governance > Reconciliation` board groups breaks by classification with counts matching API summary.
  - `Notification Center` inbox entries for unresolved high-severity breaks include playbook deep links and assigned owner.
- **Produced artifact location:** `artifacts/wave4/evidence/wave4-governance-multi-ledger-reconciliation-break-classification-v1/`
- **Regression owner:** `Governance and Ledger / Ledger Integrity Rotation`
