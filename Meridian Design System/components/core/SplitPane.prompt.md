Two-panel resizable layout for workstation rails — list | inspector, chart | blotter, tree | detail. Exactly two children. The divider is a load-bearing 1px hairline with a 7px grab zone: drag it, or focus it and use arrows (8px, Shift 40px), Home/End for min/max.

```jsx
<SplitPane initial={320} min={220} max="45%" minSecondary={360} persistKey="blotter-inspector">
  <Blotter />
  <Inspector />
</SplitPane>
```

`min`/`max`/`initial`/`minSecondary` take px numbers or `"%"` strings, and are re-clamped whenever the container resizes — a persisted 900px rail cannot survive into a 700px window. **Set `minSecondary`** whenever the second pane has a real floor (a table that stops being readable under 360px); `max` alone doesn't protect it.

`collapsible` turns double-click (and Enter on the focused divider) into a collapse toggle instead of a reset to `initial`; drive it from a chrome button with `collapsed`/`onCollapsedChange`. `persistKey` remembers the size in localStorage, written once when the drag settles. `onResize` fires continuously, `onResizeEnd` once — use the latter to sync layout to a server. `disabled` locks the divider for fixed layouts.

Nest for three-pane workstations, outer split first:

```jsx
<SplitPane initial={240} persistKey="nav">
  <NavRail />
  <SplitPane primary="end" initial={380} minSecondary={480} persistKey="inspector">
    <Blotter />
    <Inspector />
  </SplitPane>
</SplitPane>
```

The panes are the scroll containers (`overflow:auto`), so give children `height:100%` rather than their own scrollers. In print the split unrolls to stacked full-width blocks and the divider disappears.
