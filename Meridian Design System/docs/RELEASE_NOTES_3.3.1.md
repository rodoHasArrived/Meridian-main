# Release 3.3.1 — Integrity and adoption

**11 August 2026 · patch · no API change**

3.3.0 shipped three template chains that proved the Release 2–4 components compose. A review of
that package found the chains were *demonstrations*: they rendered the components in sequence but
did not adopt the operating language the system had spent 3.1 and 3.2 building, and the package's
own counts had drifted from the manifest.

Two of the three chains — `close-management` and `alm-review` — already carry the full stack.
This patch brings the third, and repairs the documentation.

## Recomposed — `templates/reconciliation-exception/`

### Before

One scrolling column. Nine steps rendered as expanded cards, each introduced by an explanatory
paragraph. The selected exception, the active step, and the completion set lived in the template's
own state. No workspace context declaration, no linked selection, no degraded-data behaviour, no
`DataStateSwitch`, and none of the standard `density` / `theme` / `environment` / `dataState`
controls — so the screen's theme depended on the environment it was opened in.

### After

```
DataStateSwitch
  └ WorkspaceContextProvider
      ├ WorkspaceContextBar          entity · book · basis · period · as-of · currency · condition
      └ LinkedSelectionProvider      scope "exception", URL-synced, committed once
          ├ queue + workflow rail    completed steps collapse to proof; blocked steps name the guard
          ├ ContinuityFrame          retained evidence around the dominant work plane
          │   └ active decision step one step expanded at a time
          └ evidence rail            coverage · linked evidence · calculation trace · audit history
      └ consequence bar              version · period · residual · unresolved gates · one action
```

| Change | Effect |
| --- | --- |
| Workflow state in the URL (`?step=&done=`) | The task resumes after a reload or a handoff |
| Completed steps collapse to a proof line | "$1,050.00 explained · $331.68 residual" replaces a re-read of the whole step |
| Blocked steps carry their guard | "Journal not posted", not a greyed row with no reason |
| One primary action, in a sticky bar | Disabled actions state the unmet condition in words |
| Operational state statement | The exception's residual and the control it blocks, not design rationale |
| Interaction colour | `--interaction-selected` / `--interaction-focus`; terracotta stays brand-only |
| Review density | 13px body, 11px minimum label, sentence-case labels, 36–44px decision rows |
| Narrow layouts | ≤1180px collapses to one plane with a Queue / Step / Evidence switch and a sticky bottom action bar |
| Every affordance live | The two secondary queue exceptions open and state their own blocking guard rather than sitting inert |

## Documentation repaired

Counts are now taken from `_ds_manifest.json`:

| Figure | Was | Is |
| --- | --- | --- |
| Templates | 28 | **31** (7 archetypes + 24 production screens) |
| Public exports | 225 | **230** |
| Modules | 172 | **173** |
| Tokens | 635 | **644** |
| Bundle | 640 KB raw / 135 KB gzip | **1,141 KB raw / ~247 KB gzip** |

Five exports were missing from the README inventory and are now listed:
`MATURITY_BUCKETS`, `SelectionHooks`, `ChangeSetHooks`, `Status`, `WorkspaceHooks`.

## Carried forward

Named, not fixed, in this patch:

- **Terracotta in interaction roles** across older components (selected table rows, worksheet
  active cells, tabs, pagination, checkboxes, date-picker selection). The token migration ledger
  and the `--brand-signal` / `--scenario-signal` rename belong to 4.0.0.
- **A decision-flow contract** (`DecisionFlowProvider` / `DecisionFlowRail` /
  `DecisionConsequenceBar`). This template still implements step state, guards and URL
  serialisation in its own logic class; that logic is the specification for the 3.5 pattern.
- **Review density defaults** inside the Release 2–3 components themselves — this patch raises
  density at the template level only.
- **Distribution** — ESM domain chunks, CSP-compatible static CSS, deduplicated `support.js`
  copies, and a runnable validator in the consumer package.
