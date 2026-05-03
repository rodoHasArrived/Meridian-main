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
dotnet run --project src/Meridian/Meridian.csproj -- --statement-validate --statement-broker samplebroker --statement-source-path <path> --statement-date <yyyy-mm-dd>
dotnet run --project src/Meridian/Meridian.csproj -- --statement-import --statement-broker samplebroker --statement-source-path <path> --statement-date <yyyy-mm-dd>
```

## Artifacts

- `reconciliation/statement-imports/*.json`: import metadata, canonical rows, raw+normalized row counts, source checksum.
- `reconciliation/cases/*.json`: reconciliation case aggregate with status history, rationale, and confidence.
