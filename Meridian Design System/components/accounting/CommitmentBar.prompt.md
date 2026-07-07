Funding state for a private-capital commitment — a hairline bar (called share filled accent, unfunded left bare) over a mono figure row: Committed · Called % · Unfunded · Distributed (DPI) · NAV (TVPI). Field names match the capital-commitment read model, so a commitment row spreads straight in.

```jsx
<CommitmentBar label="Blue Harbor Growth Fund III" vintage={2021}
  commitment={5_000_000} called={3_100_000} distributed={1_240_000} nav={2_900_000} />
```

Omit `distributed` / `nav` and those figures (and DPI/TVPI) disappear rather than showing 0. Stack several in a `PanelSurface` for a commitments panel, or render one per row inside `ExpandableDataTable` detail. For a single headline number use `MetricCard`; this is for the called/unfunded/returned decomposition.
