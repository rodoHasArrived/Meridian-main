Live/delayed/partial/offline/error state for a data surface, placed beside the evidence it describes. It states age, cadence, what is missing, and the recovery path — the visible half of the last-known-good contract.

```jsx
<ConnectionState state="live" subject="Prices" age="42s" cadence="15s" />
<ConnectionState state="partial" detail="3 of 11 tenors missing" />
<ConnectionState state="error" detail="Curve builder failed — observations still valid"
  actionLabel="Retry" onAction={retry} block />
```

Never swap a rendered view for a loading skeleton after a drop: keep the last good data, mark it, and let this strip carry the condition. Use `DataCondition` for a *value's* condition and `FreshnessIndicator` for a single feed's age inside chrome; `ConnectionState` is for the workspace surface.

Unrecognized runtime connection values read Unknown rather than claiming Live. Network activity and retaining last good data remain the parent responsibility.
