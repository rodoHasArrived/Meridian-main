import { cn } from "@/lib/utils";
import type { FigureDifferenceNote as FigureDifferenceNoteModel } from "@/lib/metric-trust";

/**
 * "Why these differ": shown when two panels on one page report different values for the same
 * quantity. It names each figure, both values and the source, as-of time and scope behind each
 * panel, so the operator does not have to reconcile the page by hand. Build the model with
 * `buildFigureDifferenceNote` from `@/lib/metric-trust`; it is null when the panels agree.
 */
export function FigureDifferenceNote({ note, className }: { note: FigureDifferenceNoteModel; className?: string }) {
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
              <th scope="row" className="py-1 pr-4 text-left font-normal text-foreground">{row.label}</th>
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
