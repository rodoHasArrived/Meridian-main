import { cn } from "@/lib/utils";

/**
 * True for a figure: an amount, count, percentage, period or identifier-like token with no spaces
 * between words ("+$500.00", "4", "12.5%", "2026-05", "(1,250.00)"). Figures keep the data face and
 * tabular digits; prose values ("Unassigned", "Packet available for review") and dates read in the
 * body face.
 */
export function isAccountingFigure(value: string): boolean {
  const trimmed = value.trim();
  return /\d/.test(trimmed) && /^[+\-−(]?[$€£¥]?[\d.,:%/\-−]+\)?$/.test(trimmed);
}

/**
 * A label/value pair in an accounting detail tile. The row wraps as a whole: a value too long to
 * sit beside its label moves below it at full width instead of breaking onto three narrow lines.
 */
export function AccountingValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  const figure = isAccountingFigure(value);
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-3 py-2">
      <span className="shrink-0 text-muted-foreground">{label}</span>
      <span className={cn("ml-auto min-w-0 break-words text-right text-foreground", figure && "font-mono tabular-nums", tone)}>{value}</span>
    </div>
  );
}
