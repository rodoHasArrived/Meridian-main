// Label/value cell shared by the Accounting screen and its extracted panel modules.
import { cn } from "@/lib/utils";

export function AccountingValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex min-w-0 items-center justify-between gap-4 px-3 py-2">
      <span className="min-w-0 flex-1 text-muted-foreground">{label}</span>
      <span className={cn("min-w-0 flex-1 break-words text-right font-mono tabular-nums text-foreground", tone)}>{value}</span>
    </div>
  );
}
