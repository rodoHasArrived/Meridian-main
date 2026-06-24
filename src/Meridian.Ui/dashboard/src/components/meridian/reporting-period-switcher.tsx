import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import {
  buildReportingPeriodOptions,
  formatReportingPeriodLabel,
  isIsoDate,
  shiftIsoDate
} from "@/lib/reporting-periods";

export interface ReportingPeriodSwitcherProps {
  asOfDate: string;
  onSelect: (asOfDate: string) => void;
  disabled?: boolean;
  className?: string;
  label?: string;
}

/**
 * Compact "as of" period control: nudge the reporting as-of date by month, jump to a
 * preset period (prev day/month/quarter/year), or pick a custom date — without leaving
 * the run surface. The component is presentational; all period math lives in
 * {@link "@/lib/reporting-periods"}.
 */
export function ReportingPeriodSwitcher({
  asOfDate,
  onSelect,
  disabled = false,
  className,
  label = "Run as of"
}: ReportingPeriodSwitcherProps) {
  const valid = isIsoDate(asOfDate);
  const presets = buildReportingPeriodOptions(asOfDate).filter((option) => option.id !== "current");

  function handleStep(amount: number) {
    if (!valid || disabled) {
      return;
    }

    onSelect(shiftIsoDate(asOfDate, "month", amount));
  }

  return (
    <div
      role="group"
      aria-label="Reporting period switcher"
      className={cn(
        "flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/25 px-3 py-2",
        className
      )}
    >
      <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
      <span className="font-mono text-sm text-foreground" aria-live="polite">
        {valid ? formatReportingPeriodLabel(asOfDate) : asOfDate}
      </span>
      <span className="flex items-center gap-1">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label="Previous month"
          disabled={!valid || disabled}
          onClick={() => handleStep(-1)}
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label="Next month"
          disabled={!valid || disabled}
          onClick={() => handleStep(1)}
        >
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </Button>
      </span>
      <span className="flex flex-wrap items-center gap-1">
        {presets.map((option) => (
          <Button
            key={option.id}
            type="button"
            variant="outline"
            size="sm"
            aria-label={`${option.label}: ${option.description}`}
            aria-pressed={asOfDate === option.asOfDate}
            disabled={disabled}
            onClick={() => onSelect(option.asOfDate)}
          >
            {option.label}
          </Button>
        ))}
      </span>
      <label className="flex items-center gap-1">
        <span className="sr-only">Custom as-of date</span>
        <Input
          type="date"
          value={valid ? asOfDate : ""}
          disabled={disabled}
          aria-label="Custom as-of date"
          className="h-8 w-[9.5rem]"
          onChange={(event) => {
            const next = event.target.value;
            if (isIsoDate(next)) {
              onSelect(next);
            }
          }}
        />
      </label>
    </div>
  );
}
