Two-panel resizable layout for workstation rails — list | inspector, chart | blotter. Exactly two children; the divider is a 1px hairline with a 7px grab zone, keyboard-resizable, double-click resets. Use `persistKey` so the operator's rail width survives reload.

```jsx
<SplitPane direction="horizontal" initial={320} min={220} max={520} primary="end" persistKey="alerts-inspector">
  <AlertTable />
  <AlertInspector />
</SplitPane>
```

`primary="end"` fixes the second pane's size (typical for right-hand inspectors). The parent must have a definite height — SplitPane fills it. Don't nest more than two levels deep.
