# Export Preflight Rules Engine

`ExportValidator` now uses a two-phase preflight pipeline so the same engine can be reused by other export domains (for example governance/report exports).

## Two-phase flow

1. **Collection phase (I/O):** `ExportValidator.CollectContextAsync` probes filesystem and source data to build `ExportPreflightContext`.
2. **Evaluation phase (pure):** `PreflightEngine<ExportPreflightContext>` applies ordered rules from `ExportPreflightRules.DefaultRules` and emits deterministic `PreflightIssue` results.

## Reusable abstractions

- `IPreflightRule<TContext>` — composable rule contract with stable `Id`.
- `PreflightIssue` — normalized issue payload with `RuleId`, `Code`, `Severity`, `Message`, `Remediation`, and optional details.
- `PreflightEngine<TContext>` — reusable evaluator for any preflight domain.

## Export rule IDs (stable)

- `export.disk-space.v1`
- `export.write-permission.v1`
- `export.data-presence.v1`
- `export.csv-complex-types.v1`

Each rule maps to legacy export validation codes (`DISK_SPACE`, `WRITE_PERMISSION`, `NO_DATA`, `CSV_COMPLEX_TYPES`) so downstream behavior stays backward compatible.

## Extension pattern for other domains

To onboard governance/report export preflight:

1. Define a domain context record (for example `GovernanceReportPreflightContext`).
2. Implement a rule set as `IReadOnlyList<IPreflightRule<GovernanceReportPreflightContext>>` with stable IDs.
3. Reuse `PreflightEngine<TContext>` in the domain validator.
4. Map `PreflightIssue` to domain API result models.

This keeps I/O probes domain-specific while preserving a shared deterministic rule engine.
