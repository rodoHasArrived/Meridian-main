# Source TODO Standard

Source TODOs must be registry-backed when they represent implementation work.

## Inline format

```csharp
// TODO(W2-TRD-001): Replace temporary replay fixture with retained replay diagnostics.
// TODO(TODO-SRC-UI-DASHBOARD-001): Add route-state coverage for Trading readiness.
```

## Registry format

Use `docs/source/data/source-todos.yml` for owner, priority, roadmap item, stage gate, source paths, and checklist items.

Validation fails when a TODO comment lacks an ID, references an unknown registry or roadmap ID, or a closed TODO still appears in registered source paths.
