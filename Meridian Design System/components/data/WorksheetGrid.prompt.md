A worksheet cell. Addressed by `"<column key><row number>"` (e.g. `"E2"`), row numbers 1-based. `type` drives presentation: `label`/`text` render in the body font (left-aligned by default); everything else renders mono + right-aligned. `formula` cells get an accent corner mark and surface their `formula` in the formula bar; `error` cells tint with the red wash.

The grid owns header and data rows, announces its active cell through aria-activedescendant,
and retains a visible grid focus indicator. A navigation key selects the first cell when
none is selected. Editing names the exact cell and returns focus to the grid on commit
or Escape without changing caller-provided financial values.
