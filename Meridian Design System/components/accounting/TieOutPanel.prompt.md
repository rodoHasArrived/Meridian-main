An explicit proof that two independently sourced values reconcile. It separates the **gross
difference**, the **explained reconciling items**, and the **unexplained residual**, so a tie-out is
never read as successful merely because the net difference is small.

```jsx
<TieOutPanel title="Custodian cash tie-out" type="one-to-one"
  sourceA={{label:"Ledger balance", value:12441284.12, condition:"confirmed"}}
  sourceB={{label:"Custodian statement", value:12439902.44, condition:"confirmed"}}
  items={[{label:"Unsettled wire received August 1", value:-1381.68, evidence:"Wire advice 4471"}]}
  tolerance={5} onInvestigate={...} onAccept={reason => ...} />
```

- `type`: `one-to-one` · `many-to-one` · `one-to-many` · `rollforward` ·
  `quantity-and-value` · `count-and-amount` · `multi-basis`.
- Status is derived: **Tied out** (residual zero), **Within tolerance** (small but unexplained —
  still requires disposition), **Exception**.
- Items carry a condition and evidence; `explained:false` marks an item as identified but not yet
  substantiated.
- `secondary` adds a second measure (units, record counts) so a value tie-out cannot hide a
  quantity break.
- Accepting an unexplained residual requires a reason by default (`requireReason`).

**A missing input is not a zero.** A source with no value — or one whose `condition` is `missing`, `pending`, `stale`, `na` or `exception` — makes the panel **indeterminate**: the gross difference and the residual render as `—`, each unusable input is named ("Custodian statement has no value"), and acceptance and resolution are unavailable. A caller-declared `status="tied"` cannot outrank the gap. Only `onInvestigate` / `onLinkEvidence` stay actionable, because closing the gap is the only forward move.

Restricted source amounts/counts and restricted reconciling amounts are withheld.
They make the proof indeterminate even when a caller declares it tied; no residual
or acceptance may be inferred from withheld inputs. This presentation safeguard
does not replace server-side permissions.

Whitespace-only numeric entries are missing, not zero. A restricted reconciling
item also withholds its supplied label/evidence so those fields cannot echo the
private amount. Authorized evidence stays available through its separate owner.
