import { cn } from "@/lib/utils";
import type { PositionValueMeasurement } from "@/lib/position-value-graphic";

/** Exact display text stays primary; the miniature is a relative scanning aid. */
export function PositionValueGraphic({ value, measurement, kind, reviewRequired = false, className }: {
  value: string;
  measurement: PositionValueMeasurement | null;
  kind: "exposure" | "pnl";
  reviewRequired?: boolean;
  className?: string;
}) {
  const signed = kind === "pnl";
  const needsReviewCue = kind === "exposure" && reviewRequired && measurement?.isLargest;
  const denomination = measurement?.currency === undefined ? measurement?.denomination
    : measurement.currency ? `${measurement.currency} (${measurement.denomination})` : `${measurement.denomination} (currency unavailable)`;
  const scaleLabel = measurement
    ? `${signed ? "P&L magnitude" : "Exposure magnitude"}: ${measurement.magnitudePct}% of largest displayed ${denomination} ${signed ? "absolute P&L" : "exposure"} among ${measurement.comparableCount} loaded positions. Display rounding retained; no currency conversion.`
    : "Graphic unavailable: this displayed amount has no supported, unambiguous format. Original amount retained.";
  return <span className="inline-flex min-w-0 flex-col items-end gap-1" title={scaleLabel}>
    <span className={cn("font-mono", className)}>{value}</span>
    {measurement ? <span className="relative block h-1.5 w-16 bg-secondary" aria-hidden="true" data-position-value-graphic={kind}>
      {signed ? <span className="absolute inset-y-[-2px] left-1/2 w-px bg-foreground/70" /> : null}
      <span className={cn("absolute inset-y-0", signed
        ? measurement.direction === "negative" ? "bg-danger" : "bg-success"
        : reviewRequired ? "bg-warning" : "bg-muted-foreground")}
        style={{
          width: `${measurement.magnitudePct / (signed ? 2 : 1)}%`,
          left: signed ? measurement.direction === "negative" ? `${50 - measurement.magnitudePct / 2}%` : "50%" : "0%"
        }} />
    </span> : <span className="text-xs font-normal text-muted-foreground">Graphic unavailable</span>}
    <span className="sr-only">{scaleLabel}</span>
    {needsReviewCue ? <span className="text-xs font-normal text-[var(--orange-dim)]">Largest · mark review</span> : null}
  </span>;
}
