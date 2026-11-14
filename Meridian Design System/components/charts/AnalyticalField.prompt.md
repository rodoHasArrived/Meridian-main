A data-bearing field for coverage, populations, quality, scenarios, workflow, or distribution. Every mark must correspond to an observation, entity, source, or condition; never use it as decorative texture.

```jsx
<AnalyticalField items={sources} columns={8} selectedId={selected}
  onSelect={setSelected} showLegend />
```

Provide a concise summary such as “94 of 100 positions confirmed; 4 provisional; 2 missing.” Keyboard arrows move through the field; Enter or Space commits a selection.
