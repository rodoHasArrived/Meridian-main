# Progressive disclosure

**Normative. 4.1.** A workstation screen holds more than any single reading needs. This document
says how depth is offered — and the one rule that governs all of it:

> **Depth is not concealment.** Every surface that withholds something states that it is
> withholding it, how much, and how to get it back. A screen may be quiet. It may never be
> misleading about what it is quiet about.

Disclosure is *arrangement*, never data context. A dial, a collapse, an override — all of it may
persist. Filters, period, as-of, scenario and entity may not ride along with it
(`SESSION_CONTINUITY.md`).

---

## 1 · Two depths, and only two

`summary` and `full`. That is the whole ladder.

- **`summary`** — the reading an operator does to decide *whether* to act: identity, the figures
  the screen is about, condition, exceptions, and totals that prove themselves.
- **`full`** — the reading an operator does to act and to defend the action: attribution,
  provenance, lineage, operator names, timestamps, prior values, reason codes.

A third level is not a third dial position — **it is a second screen.** When the reading is
forensic (a run's raw payloads, a figure's full derivation, an entity's whole amendment history),
route to a dedicated screen with its own context bar and its own URL. Nobody audits inside a
tooltip.

**Default depth is `full`.** Summary is a thing the operator chooses, not a thing the screen
imposes. The one exception: a region explicitly marked `secondary` (§3).

## 2 · The dial

One dial per screen, in the chrome beside density and never inside the work plane. Regions may
carry their own dial, which overrides the screen's for that region until reset.

```jsx
<DisclosureProvider screenId="reconciliation" defaultDetail="full">
  <WorkstationTopbar tools={<DetailDial />} />
  …
  <DisclosureRegion id="breaks" title="Breaks" dial>…</DisclosureRegion>
</DisclosureProvider>
```

`DisclosureProvider` persists the dial, region overrides and operator collapse under
`mds.disclosure.<screenId>` **and** mirrors the screen dial into `?detail=` — so a returning
operator finds their reading, and a pasted link carries the sender's reading to the recipient.
URL wins on load; it is the more deliberate of the two.

Components never read the context themselves. A screen passes depth down —
`detail={d.detailFor("breaks")}` — so a grid remains a pure function of its props and a card can
render at a depth the screen isn't at.

## 3 · What withdraws, and what never does

Tier a column, field or row `tier: "full"` and it withdraws at summary depth. The list of things
that **must not** carry a tier is short and absolute:

| Never withdraws | Because |
| --- | --- |
| Identity — name, id, version, as-of | an object you can't name is an object you can't act on |
| The figures the surface is *about* | a P&L screen without P&L is a different screen |
| Data condition and its marks | a stale or estimated number is stale at every depth (`VALUE_ENVELOPE.md`) |
| Exceptions, breaks, failures | an unreported break is a lie by arrangement |
| Arithmetic proof (`Σ debit = Σ credit`) | a table that stops proving itself stops being evidence |
| Anything blocking the primary action | disclosure never gates work |

Good candidates for `tier: "full"`: attribution splits, secondary identifiers (CUSIP beside ISIN),
operator and approver names, per-leg detail behind a total, prior values, adapter/run internals,
reason codes, non-blocking commentary.

When columns withdraw, `DataGrid` prints `N columns withheld at summary depth · Show all` beneath
itself. Same law in `KeyValueGrid`, `EntitySummary`, and `DetailOnly`.

## 4 · Collapse, and the residue a collapsed region owes

`DisclosureRegion` collapses a secondary region — but its header is a **standing report** that
renders identically open or closed:

```jsx
<DisclosureRegion id="aged" title="Aged items" secondary
  count={31} countLabel="items" severity="warning" severityLabel="7 over SLA"
  freshness="09:41 ET">…</DisclosureRegion>
```

Three facts, always: **how much** (count), **how bad** (worst severity in the region), **how old**
(freshness of what it holds). A region with a critical item inside may not present a neutral
header. If a region cannot state its own residue, it is not a candidate for collapse.

Ordering: `secondary` regions start collapsed at summary depth and open at full depth. Once the
operator toggles one, their pin outranks the dial for the rest of the session —
`reset()` returns the screen to dial-following.

## 5 · Drill: row → drawer → screen

Three tiers, in this order, each earning the next:

1. **Inline expand** (`expand`) — the row's own detail, in place, context preserved. First reach.
   For legs, splits, the two contributing values behind a difference.
2. **Drawer** (`onDrill` → `Drawer`) — the object's full detail while the plane stays behind it.
   For everything that would otherwise be a page you must come back from.
3. **A screen** — audit tier only: full lineage, raw payloads, amendment history. It gets a URL,
   a context bar, and a breadcrumb home.

Never skip a tier to reach for the next: a drawer that shows what an inline expand could have
shown costs the operator their place, and a screen that shows what a drawer could have shown costs
them their work.

Hover is a **peek, never a path** — it may preview (`ValueEnvelope`'s hover envelope), it may never
be the only way to something. Every hover disclosure has a click equivalent in the same place.

## 6 · Print and export

Print restores **everything**. A printed screen is evidence; the depth an operator happened to be
reading at is not a property of the document. Regions render open, tiered columns return, and the
withheld strip disappears. Same for export: a CSV is never a summary of a table unless the
operator asked for a summary.

## 7 · Do → don't

- **Do** state the count of what is hidden. **Don't** hide a count.
- **Do** collapse commentary. **Don't** collapse an exception.
- **Do** put the dial in the chrome. **Don't** scatter per-panel dials as the primary control.
- **Do** default to `full`. **Don't** ship a screen that opens at summary because it looks calmer.
- **Do** keep hover previews. **Don't** make hover the only route to a fact.
- **Do** send the audit tier to its own screen. **Don't** nest a third depth inside a drawer.

## Reference

`components/core/Disclosure.jsx` — `DisclosureProvider`, `DetailDial`, `DisclosureRegion`,
`DisclosureResidue`, `DetailOnly`, `useDisclosure`, `atDepth`, `severityOf`.
Specimen: `components/core/progressive-disclosure.card.html`.
Tiered consumers: `DataGrid` (`columns[].tier`, `detail`, `expand`, `onDrill`), `KeyValueGrid`,
`EntitySummary`.
