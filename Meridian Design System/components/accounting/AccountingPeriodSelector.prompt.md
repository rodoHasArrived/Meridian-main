Use instead of a date picker when the user is choosing a posting or reporting period. It understands open, soft-closed, adjustment, and locked states and keeps the permission consequence visible.

```jsx
<AccountingPeriodSelector periods={periods} value={period} onChange={setPeriod}/>
```

The selected period’s state and description remain adjacent to the control. Reopen behavior belongs in the consuming workflow, not hidden inside the selector.
