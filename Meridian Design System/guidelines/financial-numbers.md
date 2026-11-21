# Financial figures in the workstation

Financial meaning takes precedence over visual uniformity. A debit, a credit, a
negative balance and an investment loss are different concepts.

- Use the body sans-serif face with tabular digits for figures. Reserve monospace
  for identifiers, codes and technical references; do not apply it to explanatory prose.
- Use `@/lib/format` for workstation display. Accounting statements intentionally
  use `components/accounting/money.ts` for parenthesized negatives and zero dashes.
  Preserve exact decimal text in the financial workflows that already require it.
- Name the unit and currency. Never assume USD because a source omitted currency.
  Do not add values across currencies without a declared, governed conversion basis.
- Select `formatPercent` for percentage units and `formatRatioAsPercent` for fractions.
  The producer's unit matters; a field name alone does not prove it.
- Use loss color only when the source meaning establishes a loss. Signed positions,
  account balances and credit postings do not become losses because of their sign.
- Linked amounts use a neutral dotted underline. Keep normal keyboard focus styling
  and a clear accessible action name. A link is not a severity indicator.
- Headline figures declare their measure, basis, population and as-of context.
  Missing context is visible, never filled from an unrelated account or strategy run.
- Changes require a producer-declared comparison interval. The shared metric renderer
  withholds legacy changes without an interval instead of assuming a day or week.
- Compare matching scopes, currencies, bases and date ranges. Exposure and market
  value are different measures. Show differing contexts or an unresolved discrepancy;
  never force totals to agree by replacing one with the other.
- Open amount proof through the existing subject-addressed evidence contract. Names,
  matching symbols and navigation links are not retained proof. Unresolved support
  remains review-required. General passport expansion follows the W10/W11 roadmap.
- Keep dates on one line in dense tables and field values; use contained scrolling
  where necessary. Prose wraps at word boundaries and remains reachable at zoom.

CSV exports use original typed values, selected columns and the complete authorized
filtered population. Label source scope and retain decimal precision. Protect textual
spreadsheet formulas without changing numeric negative amounts. A partial page is
never labeled as a complete export.

The dashboard's `npm run check:workstation` checks exact legacy JSX violations for
browse tables, local display formatting and literal status badges. New violations
fail the build. Approved statement and editing modules are explicitly listed; these
exceptions do not permit new browse tables. Legacy entries may only shrink during
the migration, and the baseline must not be expanded to obtain a passing build.
