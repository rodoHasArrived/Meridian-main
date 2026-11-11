# WorkspaceLayout

Screen-level arrangement identity. Track A of `guidelines/SESSION_CONTINUITY.md`.

```jsx
<WorkspaceLayoutProvider screenId="ops-wall"
  regions={[{id:"queue",size:1.4},{id:"case",size:1},{id:"evidence",size:.8,collapsed:true}]}
  layouts={[{name:"Breach triage",regions:{evidence:{collapsed:false,size:1.2}}}]}>
  <WorkspaceLayoutBar />
  <WorkspaceLayoutGrid direction="row" style={{height:"calc(100vh - 130px)"}}>
    <LayoutRegion id="queue" label="Exception queue" minSize={.18}><CaseQueue …/></LayoutRegion>
    <LayoutRegion id="case" label="Case" onPopOut={detach}><ResolutionDrawer …/></LayoutRegion>
    <LayoutRegion id="evidence" label="Evidence"><EvidenceInspector …/></LayoutRegion>
  </WorkspaceLayoutGrid>
</WorkspaceLayoutProvider>
```

## From markup (design-component templates)

A template cannot pass `LayoutRegion` elements — an `<x-import>` mounts a component, so the grid
would see mounts instead of regions. Declare the regions positionally instead and let the grid wrap
each child:

```html
<x-import component-from-global-scope="Meridian….WorkspaceLayout"
  screen-id="adapter-operations" regions="{{ regions }}" layouts="{{ layouts }}" slots="{{ slots }}">
  <div>…first region's content…</div>
  <div>…second region's content…</div>
</x-import>
```

`slots` takes `{id, label, minSize, collapsible, chrome, onPopOut}` per child, in order. Everything
else — persistence, dirty state, collapse rails, keyboard dividers — behaves identically.

## Rules

- **Arrangement only.** `size`, `collapsed`, `hidden`. Anything else in a `setRegion` patch is
  filtered out. Filters, selection, period and as-of belong to `WorkspaceContextBar`, `SavedViews`
  and the URL.
- **One key per screen** — `mds.workspace-layout.<screenId>`. Never share a key across screens:
  regions differ and the arrangement half-applies.
- **Dirty is visible, saving is deliberate.** `WorkspaceLayoutBar` shows *Modified* plus *Save* and
  *Reset*; the provider never overwrites a named layout on its own.
- **`size` is a flex weight**, not pixels or a percentage. Two regions at `1` and `1.4` split
  10 : 14. `minSize` is a share of the grid (`0.1` = 10%).
- **Collapse keeps identity.** A collapsed region becomes a labelled rail that is focusable and
  expandable by keyboard. Do not render regions conditionally to "hide" them — set `hidden`, so the
  layout can put them back.
- **`onPopOut` is a contract, not a button.** The host must keep the detached region inside the same
  `LinkedSelectionProvider` and workspace context. Omit the prop if it cannot.
- Dividers are `role="separator"`, `tabIndex=0`, arrow-key resizable, and report `aria-valuenow`
  plus an `aria-valuetext` that names both regions and their shares.
- **A hidden region is always recoverable.** The bar renders *Show N hidden* whenever any region is
  hidden; collapse and hide both keep a way back, per the law's rule 5.
- Print drops the bar and the dividers and stacks the regions.

## Composition

Sits directly under `WorkspaceContextBar` inside `WorkstationShell`. Nest a `column` grid inside a
`row` grid for a wall. Use `SplitPane` only *inside* a region — never as the screen's outer
arrangement, which has no identity to save.
