# Fund Account Traversal (Authoritative)

Authoritative account resolution for fund-scoped account queries is:

1. `Fund` node (`FundSummaryDto.FundId`)
2. `OwnershipRelationship` edge where `RelationshipType == Owns`
3. `AccountDefinition` (`AccountSummaryDto`) for each owned account

Filtering rules:

- `AccountType` filter applies to `AccountSummaryDto.AccountType`.
- `Status` filter maps to account activity:
  - `active` (default): `AccountSummaryDto.IsActive == true`
  - `all`: no activity filtering
  - `suspended`/`closed`: retained for API compatibility and treated as non-default broad queries

Implementation lives in `src/Meridian.Identity/FundStructure/FundAccountTraversalQueryService.cs`
under the `Meridian.Identity` namespace and is reused by
`/api/funds/{fundId}/accounts`.
The query service caches per-fund traversal snapshots for 30 seconds to protect high-frequency callers
(e.g., fund account lists and reconciliation break workflows) from repeated full graph scans.
