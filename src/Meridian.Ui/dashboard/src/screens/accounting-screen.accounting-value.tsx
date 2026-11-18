// Label/value cell shared by the Accounting screen and its extracted panel modules. It follows the
// detail pane's value rules: figures and identifiers use the data face and never wrap, dates are
// dates (no invented "00:00 UTC"), and prose reads as prose.
import { cn } from "@/lib/utils";
import { inferDetailFieldKind, usesDataFace } from "@/components/meridian/detail-pane.view-model";
import { formatAccountingDate } from "@/screens/accounting-screen.formatting";

export function AccountingValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  const kind = inferDetailFieldKind(value);
  const display = kind === "date" && /^\d{4}-\d{2}-\d{2}/.test(value.trim()) ? formatAccountingDate(value) : value;
  const dataFace = usesDataFace(kind, display);
  const figure = kind === "number" || kind === "date";
  // In a narrow cell the value moves under its label instead of squeezing either one: a figure
  // stays on one line, and text wraps at word boundaries.
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-0.5 px-3 py-2">
      <span className="min-w-0 text-muted-foreground">{label}</span>
      <span
        data-detail-value={kind}
        className={cn(
          "ml-auto min-w-0 text-right text-foreground",
          figure ? "whitespace-nowrap tabular-nums" : "break-words",
          dataFace && "font-mono",
          tone
        )}
      >
        {display}
      </span>
    </div>
  );
}
