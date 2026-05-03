# Statement Reconciliation Runbook

## Workflow

1. Validate statement inputs before persistence.
2. Import statement rows and immutable source metadata.
3. Run reconciliation and triage unresolved cases.
4. Resolve with linked corrective evidence, or waive with explicit justification.

## CLI

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --statement-validate --statement-source-kind local --statement-source-path ./incoming/statements/ibkr-jan.csv
dotnet run --project src/Meridian/Meridian.csproj -- --statement-import --statement-source-kind local --statement-source-path ./incoming/statements/ibkr-jan.csv
dotnet run --project src/Meridian/Meridian.csproj -- --statement-reconcile --statement-source-kind local --statement-source-path ./incoming/statements/ibkr-jan.csv
```

## Guardrails

- Waive only when the variance is operationally acknowledged and no corrective ledger/order action is required.
- Resolve when accounting or trade evidence exists and can be linked to a deterministic corrective entry.
- Every state transition must include operator identity, timestamp, and rationale note.
- Re-runs must preserve prior evidence; append new events rather than rewriting case history.
