ErrorBoundary — panel-level crash isolation. Wrap each independent workstation panel (chart, table, inspector) so one render crash degrades to a quiet fault panel instead of blanking the screen. "Try again" clears the error and re-renders the children.

```jsx
<ErrorBoundary label="Equity curve">
  <EquityCurve data={series} />
</ErrorBoundary>
```
