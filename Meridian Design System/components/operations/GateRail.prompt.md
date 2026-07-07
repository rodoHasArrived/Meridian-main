The operations pipeline as a connected horizontal stepper — ingest → mapping → proof → review → approval, or whatever gate sequence your workflow has. Each node tints by status; the connector into a node turns green once the prior gate Passed.

```jsx
<GateRail gates={[
  { key: "BrokerIngest", label: "Broker ingest", status: "Passed" },
  { key: "SecurityMaster", label: "Security master", status: "Passed" },
  { key: "LedgerPosting", label: "Ledger posting", status: "InProgress" },
  { key: "Reconciliation", label: "Reconciliation", status: "ReviewRequired" },
  { key: "Approval", label: "Approval", status: "NotStarted" },
]} />
```

Pair with a `SeverityBadge` + proof-state `Callout` and gate the primary action button on the last gate's status (see PATTERNS.md § Strategy Executor patterns → Review gating).
