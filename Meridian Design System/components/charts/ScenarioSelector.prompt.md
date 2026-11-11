Use `ScenarioSelector` for a small set of modeled alternatives whose consequences
should be visible before selection. The caller owns the selected id and resulting
calculations; selecting a scenario must not execute a trade or post a journal.

```jsx
<ScenarioSelector ariaLabel="Synthetic rate scenarios" value={scenario}
  onChange={setScenario}
  scenarios={[{ id: "base", label: "Base", headline: "4.2%",
    spark: [100, 101, 102], sub: "Synthetic model" },
    { id: "stress", label: "Stress", headline: "−3.1%", tone: "negative",
    spark: [100, 98, 97], sub: "Parallel rate shock" }]}
  note="Model outcomes; state horizon, basis, and assumptions beside the selector." />
```

Use stable unique ids. Omit `onChange` for a noninteractive printed record; items
then expose disabled radio semantics. Empty scenarios render an empty group, so
the parent must state why no alternatives are available. Keep model output
separate from observed data and show stale/missing model inputs outside this
selector; it does not validate the model or persist the choice.

The current keyboard handler supports Up/Down/Left/Right selection through the
controlled callback. It does not move focus to the newly selected item and does
not implement Space/Enter on its div-based radios. Validate or supply a complete
radio interaction before adopting it in a keyboard-dependent production flow;
the presence of ARIA roles alone does not establish accessibility.
