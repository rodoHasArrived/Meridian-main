# C#/TypeScript contract parity registry

Run the positional-record comparison from the repository root:

```bash
python build/scripts/ci/check-contract-type-parity.py --json-output build/reports/contract-type-parity.json
```

`type-parity-registry.json` owns the enforced pairs and review exemptions. The JSON report includes
each exempt pair's current diagnostics in `divergence_details`. Promote an exemption into `pairs`
only when the repaired checker reports agreement; keep remaining differences visible for review.

## Exemption reassessment, 2026-10-15

All 38 existing exemptions were reevaluated after preserving C# array suffixes during attribute
removal and excluding comments from parameter types. `ProviderSetupResult` now agrees and moves
into the enforced set: both reported differences were nullable arrays misread as scalars. There
are now **103 enforced pairs and 37 exemptions**, with **161 reported declaration differences**.
No application contract was changed to satisfy the checker.

The following recorded diagnostic counts changed. The other 31 exempt entries were reevaluated
and retained their counts. The registry stores the current count for every retained entry.

| Pair | Previous count | Current count | Assessment |
| --- | ---: | ---: | --- |
| `ProviderSetupResult` | 2 | 0 | Array parser artifacts; now enforced. |
| `ProviderSetupRequest` | 4 | 3 | Array shape agrees; `kind`, `displayName`, and `capabilities` still differ in declared nullability. |
| `StrategyRunDetail` | 3 | 2 | Comment-induced collection mismatch removed. The remaining reported members exist in the C# record body, outside this checker's positional scope. |
| `WorkstationSecurityReference` | 11 | 10 | A comma in `RiskCountry` documentation previously created a spurious `CommonTerms` parameter; comment removal eliminates it. |
| `ReconciliationBreakQueueItem` | 27 | 28 | Refresh of stale count; current source also differs on `version` nullability. |
| `ResolveReconciliationBreakRequest` | 1 | 2 | Refresh of stale count; current source also differs on `resolvedBy` nullability. |
| `ReviewReconciliationBreakRequest` | 1 | 3 | Refresh of stale count; current source also differs on `assignedTo` and `reviewedBy` nullability. |

These counts describe the checker's findings, not confirmed API defects. Its limits matter when
triaging the retained entries:

- It reads positional record parameters, not public properties declared in record bodies. For
  example, `StrategyRunDetail.EvidenceLoop` and `.AcceptanceChecklist`, and
  `VerifiedOperationOutcome.SchemaVersion` and `.IsSuccessful`, exist in C# even though the checker
  reports their TypeScript members as having no counterpart. Keep these exemptions until body
  properties are covered and their complete serialized shapes are compared.
- It treats TypeScript optional members as nullable and does not model C# constructor defaults.
  `CreateLedgerBookRequest`, for example, supplies defaults for all three fields it reports.
  Request omission/default behavior needs serialization or endpoint evidence before changing
  either contract.
- It compares member presence, top-level nullability, and collection shape. It does not prove
  scalar or element-type equivalence, account for custom serialization, or establish whether
  extra UI fields are deliberate enrichments. Review those behaviors against the actual route.

The 37 retained entries remain review candidates; this reassessment does not classify them all
as application contract defects or remove their exemptions without evidence.
