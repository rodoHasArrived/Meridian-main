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

The selector uses native buttons with radio semantics. Tab enters the selected
option (or the first option when the selection is absent or no longer available).
Arrow keys wrap through options, moving focus and calling `onChange`; Home/End
move to the first/last option. Space/Enter activate the focused option through
the native button behavior. The caller must update `value` to reflect selection.
An unavailable selection remains unselected until the operator chooses an option;
the fallback Tab stop never silently changes the model. Printed records remain
disabled and outside the Tab order.
