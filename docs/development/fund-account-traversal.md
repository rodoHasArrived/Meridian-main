# Fund Account Traversal (Authoritative)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-23

Authoritative account resolution for fund-scoped account queries is:

1. `Fund` node (`FundSummaryDto.FundId`)
2. `OwnershipRelationship` edge where `RelationshipType == Owns`
3. `AccountDefinition` (`AccountSummaryDto`) for each owned account

Filtering rules:

- `AccountType` filter applies to `AccountSummaryDto.AccountType` (case-insensitive). An
  unrecognized value is ignored rather than rejected, so the query returns all account types.
- `Status` filter maps to account activity:
  - `active` (default): `AccountSummaryDto.IsActive == true`
  - `all`: no activity filtering
  - `suspended`/`closed`: retained for API compatibility and treated as non-default broad queries

Implementation lives in `src/Meridian.Identity/FundStructure/FundAccountTraversalQueryService.cs`
under the `Meridian.Identity` namespace and is reused by
`/api/funds/{fundId}/accounts`.
Access control on `/api/funds/{fundId}/accounts` (`GetFundAccountsByOwnershipTraversal`):

- An unauthenticated caller receives `401`; the caller must hold `AdminMaintenance` or
  `ManageDirectLending`, otherwise `403`.
- The caller must also pass the scoped fund-access check for the requested `fundId` before the
  traversal runs.
- If `IFundAccountTraversalQueryService` is not registered, the endpoint returns a `501`
  problem response ("Fund account service is not registered.").

The query service caches per-fund traversal snapshots for 30 seconds to protect high-frequency callers
(e.g., fund account lists and reconciliation break workflows) from repeated full graph scans.
