export interface FigureReference {
  label: string;
  /** Exact producer-provided value; the UI does not round or reconcile totals. */
  value: string;
  measure: string;
  basis: string;
  scope: string;
  currency: string;
  asOf: string;
}

export function explainFigureDifference(left: FigureReference, right: FigureReference): string | null {
  if (left.value === right.value && left.currency === right.currency) return null;
  const differences = [
    left.measure !== right.measure ? "measure" : null,
    left.basis !== right.basis ? "basis" : null,
    left.scope !== right.scope ? "population" : null,
    left.currency !== right.currency ? "currency" : null,
    left.asOf !== right.asOf ? "as-of time" : null
  ].filter(Boolean);
  const incomplete = [left, right].some((figure) =>
    [figure.measure, figure.basis, figure.scope, figure.currency, figure.asOf].some((item) => !item.trim()));
  if (incomplete) return "These figures differ. Their comparison context is incomplete; review the source basis before comparing totals.";
  if (differences.length) return `These figures use different ${differences.join(", ")}. They are not directly comparable.`;
  return "These figures differ despite matching declared context. Review the source records; the difference has not been reconciled.";
}

/** Differences remain visible; opening the disclosure explains both exact operands. */
export function FigureDifferenceNote({ left, right }: { left: FigureReference; right: FigureReference }) {
  const explanation = explainFigureDifference(left, right);
  if (!explanation) return null;
  return (
    <aside className="border-l-2 border-warning px-3 py-2 text-xs" aria-label="Figure comparison note">
      <p>{explanation}</p>
      <details className="mt-1">
        <summary className="cursor-pointer font-medium focus-visible:outline focus-visible:outline-2">Compare figure bases</summary>
        <dl className="mt-2 grid gap-2 sm:grid-cols-2">
          {[left, right].map((figure, index) => (
            <div key={index}>
              <dt className="font-semibold">{figure.label}</dt>
              <dd className="tabular-nums">{figure.value} {figure.currency || "Currency unavailable"}</dd>
              <dd>{figure.measure || "Measure unavailable"} · {figure.basis || "Basis unavailable"}</dd>
              <dd>{figure.scope || "Population unavailable"} · {figure.asOf || "As-of time unavailable"}</dd>
            </div>
          ))}
        </dl>
      </details>
    </aside>
  );
}
