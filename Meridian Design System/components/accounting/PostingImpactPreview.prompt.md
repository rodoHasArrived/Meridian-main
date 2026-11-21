Show the consequences before a journal or adjustment is posted: balanced debit/credit lines, pre/post balances, basis effects, downstream reports/controls, period state, and reversal behavior.

```jsx
<PostingImpactPreview period="2026-07" periodState="open" lines={lines}
  effects={effects} downstream={["Trial balance","STAT schedule"]} onPost={post}/>
```

Posting is disabled for an unbalanced journal or locked period. Use explicit account and reporting consequences rather than a generic confirmation dialog.

Empty/all-zero or invalid supplied sides cannot be posted. Warning acknowledgement binds the exact current preview, including amounts, period, authority and checks; changing that context revokes the acknowledgement. Validation/authorization/idempotency remain server responsibilities.
