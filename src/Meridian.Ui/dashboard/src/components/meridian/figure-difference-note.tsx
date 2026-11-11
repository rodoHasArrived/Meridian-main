import { cn } from "@/lib/utils";
import type { FigureDifferenceNote as FigureDifferenceNoteModel } from "@/lib/metric-trust";

/**
 * "Why these differ": shown when two panels on one page report different values for the same
 * quantity. It names each figure, both values and the source, as-of time and scope behind each
 * panel, so the operator does not have to reconcile the page by hand. Build the model with
 * `buildFigureDifferenceNote` from `@/lib/metric-trust`; it is null when the panels agree.
 */
function RetainedFigureDifferenceNote({ note, className }: { note: FigureDifferenceNoteModel; className?: string }) {
  const [first, second] = note.sources;

  return (
    <section
      role="note"
      aria-label={note.title}
      className={cn(
        "rounded-[2px] border border-border border-l-[3px] px-3.5 py-3 text-sm",
        note.unexplained ? "border-l-warning bg-warning/10" : "border-l-primary bg-primary/10",
        className
      )}
    >
      <p className="font-semibold leading-5 text-foreground">{note.title}</p>
      <p className="leading-5 text-muted-foreground">{note.summary}</p>
      <table className="mt-2 w-full max-w-2xl text-xs" aria-label="Figures that differ">
        <thead>
          <tr className="text-left text-muted-foreground">
            <th scope="col" className="py-1 pr-4 font-medium">Figure</th>
            <th scope="col" className="py-1 pr-4 text-right font-medium">{first?.name}</th>
            <th scope="col" className="py-1 text-right font-medium">{second?.name}</th>
          </tr>
        </thead>
        <tbody>
          {note.rows.map((row) => (
            <tr key={row.key} className="border-t border-border/60">
              <th scope="row" className="py-1 pr-4 text-left font-normal text-foreground">
                {row.label}
                {row.detail ? <span className="block text-muted-foreground">{row.detail}</span> : null}
              </th>
              <td className="py-1 pr-4 text-right font-mono tabular-nums text-foreground">{row.first}</td>
              <td className="py-1 text-right font-mono tabular-nums text-foreground">{row.second}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <dl className="mt-2 space-y-0.5 text-xs leading-5 text-muted-foreground">
        {note.sources.map((source) => (
          <div key={source.name} className="flex flex-wrap gap-x-2">
            <dt className="font-medium text-foreground">{source.name}</dt>
            <dd>{source.text}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

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
function ExactFigureDifferenceNote({ left, right }: { left: FigureReference; right: FigureReference }) {
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

export function FigureDifferenceNote(props: { note: FigureDifferenceNoteModel; className?: string } | { left: FigureReference; right: FigureReference }) {
  return "note" in props ? <RetainedFigureDifferenceNote {...props} /> : <ExactFigureDifferenceNote {...props} />;
}
