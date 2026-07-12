Toolbar — the standard band above a data surface: search/filters at the start, actions at the end. Compose with ToolbarGroup (gapped cluster), ToolbarSpacer (pushes what follows to the end), and ToolbarDivider (vertical hairline).

```jsx
<Toolbar>
  <ToolbarGroup><Input placeholder="Search runs…" /><SegmentedControl options={["All","Live","Paper"]} value="All" /></ToolbarGroup>
  <ToolbarSpacer />
  <ToolbarGroup><Button variant="ghost">Export CSV</Button><Button variant="primary">New run</Button></ToolbarGroup>
</Toolbar>
```
