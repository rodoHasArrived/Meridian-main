# Session continuity

Status: **law, additive.** Introduced 4.1 (track A of `docs/UX_DEPTH_4_1.md`). No existing API changes behaviour.

A workstation is not a page. The operator leaves it — for a meeting, for a shift, for three days in the
middle of a close — and comes back, sometimes on a different monitor. Meridian composed screens well and
remembered nothing about the person who left one. `SplitPane` is pane-local, `SavedViews` is table-local,
`ColumnManager` persists columns; no surface held the **session**.

This document is the contract a screen owes a **returning** operator. Three primitives carry it:

| Obligation | Operator question | Primitive |
|---|---|---|
| **Arrangement** — where things were | "Is my wall the way I left it?" | `WorkspaceLayout` (shell) |
| **Traversal** — what I was looking at | "Where was I before this?" | `ObjectTrail` (core) |
| **Delta** — what moved while I was gone | "I was out four hours. What changed?" | `HandoverBrief` (operations) |

A screen may omit an obligation only when it has no state to remember (`session-start` has no
arrangement to restore) or is a print deliverable (`report-pack`). Omission is a decision, not an
oversight: record it in the template's contract block.

## The separation that must not blur

Continuity is **not** context. `WorkspaceContextBar` owns entity, book, basis, period, as-of, currency
and scenario; `TimeContextProvider` owns the twelve time roles; the URL owns what is shareable. Session
continuity owns only *arrangement*, *traversal* and *delta*.

| Surface | Remembers | Never remembers |
|---|---|---|
| `WorkspaceLayout` | region size, collapse, visibility, active layout name | filters, selection, period, as-of, sort |
| `ObjectTrail` | object refs visited, with the version viewed | the object's data |
| `HandoverBrief` | a last-seen mark per operator | what the operator concluded |

A layout that carries data context makes a stale figure look intentional. This is the single most
important prohibition in this document.

## Arrangement — `WorkspaceLayout`

1. **Named, never implicit.** A restorable arrangement is a named layout with an explicit `Default`.
   An unnamed remembered state cannot be shared, reset, or reasoned about — and an operator who cannot
   say "put it back" will stop rearranging anything.
2. **One store, not a parallel one.** Persist through the same mechanism as `SavedViews` and
   `ColumnManager`: `localStorage`, one key per screen id (`mds.workspace-layout.<screenId>`). A
   component writes only its own key.
3. **Arrangement keys only.** `size`, `collapsed`, `hidden`. The provider filters any other key out of
   a region patch rather than trusting callers.
4. **Dirty is visible; saving is deliberate.** A modified layout states that it differs from the saved
   one and offers *Save* and *Reset*. Meridian does not silently overwrite a named arrangement.
5. **A collapsed region keeps its identity.** It collapses to a labelled rail that can be focused and
   expanded by keyboard — never to nothing. An operator must never lose a pane by dragging.
6. **Dividers are operable without a mouse.** `role="separator"`, focusable, arrow keys resize,
   `aria-valuenow` reports the percentage. This is a conformance requirement, not a nicety.
7. **Pop-out preserves linkage.** A region opened in its own window stays inside the same
   `LinkedSelectionProvider` and the same workspace context, or it does not open. A detached pane that
   silently stops following selection is worse than no pop-out at all.

## Traversal — `ObjectTrail`

1. **An entry is a reference, not a snapshot**: `{type, id, version, label}`. The trail stores what the
   operator looked at, never a copy of the data.
2. **Returning to a moved version is a navigation with a notice.** If the entry's version is no longer
   current, the trail says so — before or as the object opens — and the receiving surface renders the
   change. Silently loading newer data under an older heading is forbidden: it is the mechanism by
   which an operator quotes a number that no longer exists.
3. **Pinning is intent; recency is behaviour.** Keep them visually separate. Recency decays and may be
   cleared; a pin survives until the operator removes it.
4. **Type before label.** Every entry names its object type from `ObjectModel`, because "July" is a
   period, a journal batch, and a report pack in the same session.
5. **The trail is per operator, never per team**, and it is not an audit log. `EventTimeline` records
   what happened to an object; the trail records where a person went.

## Delta — `HandoverBrief`

1. **The mark is explicit.** The brief states the window it covers (`since` → now) and how long that is.
   "Recent" is not a window.
2. **Four kinds, fixed order**: `breached` → `assigned` → `changed` → `resolved`. Severity orders rows
   inside a kind. What broke outranks what arrived; what arrived outranks what moved.
3. **Ownership is the filter.** A brief covers objects the operator owns or watches. A brief that
   reports everything is a firehose and will be dismissed on sight.
4. **Every row carries its own clock and its own actor.** A change with no time and no author cannot be
   handed over.
5. **Acknowledgement moves the mark, and nothing else.** It is not an approval, not an attestation, and
   never a bulk action on the objects listed.
6. **Empty is a statement.** "Nothing moved on your 14 objects since 09:12" is the correct render —
   never a blank panel, which reads as a failed load.

## Template obligations

A template that adopts continuity declares it, the way it already declares density and environment:

```jsx
<WorkspaceLayoutProvider screenId="ops-wall" regions={REGIONS} layouts={PRESETS}>
  <ObjectTrailProvider screenId="ops-wall" onNavigate={open}>
    …
```

A design-component template declares its regions positionally instead, because an `<x-import>`
mounts a component and the grid would otherwise see mounts rather than regions:

```html
<x-import component-from-global-scope="Meridian….WorkspaceLayout"
  screen-id="ops-wall" regions="{{ regions }}" layouts="{{ layouts }}" slots="{{ slots }}">
```

Both forms are normative. A continuity primitive that only composes through React children cannot be
adopted by a template, and an unadoptable law is not a law — see `guidelines/EXTENDING.md`.

Conformance for a new or revised screen:

- The screen has a `screenId` and one `Default` layout that a fresh operator lands on.
- Reload restores arrangement without a save action, and restores nothing about data context.
- Back returns the previous object at the version it was viewed at, with a notice when that moved.
- If the screen owns queues, cases, or breaches, it can answer "what changed since I left".
- Every divider and every collapsed rail is reachable and operable by keyboard.

## Anti-patterns

- **Auto-saving over a named layout.** Destroys the arrangement the operator asked to keep.
- **A trail of URLs.** A URL is not an object reference; it cannot state a version.
- **A brief that recomputes on open.** The window must be anchored to the last-seen mark, or two
  consecutive opens disagree about what "since" means.
- **Restoring selection.** A restored selection acts on the operator's behalf on stale data.
- **One layout store shared across screens.** Screens have different regions; a shared key produces
  arrangements that half-apply.
