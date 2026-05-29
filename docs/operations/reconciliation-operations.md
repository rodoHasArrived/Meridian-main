# Reconciliation Operations

## Supported schema

Current implementation supports one broker statement schema: `samplebroker` CSV with header:

`account,symbol,quantity,price,cashAmount,activityType,tradeDate`

## Operator workflow

1. Validate statement shape and required fields.
2. Import statement rows into canonical format with persisted source checksum.
3. Run matching (position/cash/transaction linkage) and review confidence + rationale.
4. Open reconciliation cases for unmatched rows and progress case lifecycle.

## Commands

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --statement-validate --statement-source-kind local --statement-source-path <path>
dotnet run --project src/Meridian/Meridian.csproj -- --statement-validate --statement-broker samplebroker --statement-source-path <path> --statement-date <yyyy-mm-dd>
dotnet run --project src/Meridian/Meridian.csproj -- --statement-import --statement-broker samplebroker --statement-source-path <path> --statement-date <yyyy-mm-dd>
dotnet run --project src/Meridian/Meridian.csproj -- --statement-reconcile --statement-source-kind local --statement-source-path <path>
```

Use the `--statement-broker` / `--statement-date` form when importing the supported
`samplebroker` canonical CSV schema. The generic `--statement-source-kind local` form is the
local-file accessibility and reconciliation probe; non-local statement adapters are not registered
in the current command surface.

## Workstation statement-run staging

The workstation statement-run creation API accepts only regular files staged under the configured
reconciliation import root before the request is submitted. Set
`MERIDIAN_STATEMENT_IMPORT_ROOT` to the approved operator drop folder for workstation uploads; when
it is not set, the local host defaults to
`data/reconciliation/statement-import-staging` under the application base directory. The API
canonicalizes the submitted path, verifies it remains inside that root, rejects directories and
links, enforces a 100 MB per-file bound, and computes the SHA-256 hash server-side instead of
trusting caller-provided checksum metadata. The `ImportedBy` audit field is also derived from the
authenticated workstation session rather than from the request payload.

## Artifacts

- `reconciliation/statement-imports/*.json`: import metadata, canonical rows, raw+normalized row counts, source checksum.
- `reconciliation/cases/*.json`: reconciliation case aggregate with status history, rationale, and confidence.
