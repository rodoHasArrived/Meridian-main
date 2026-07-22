import { memo } from "react";
import { ShieldAlert, ShieldCheck, ShieldQuestion } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { BiasDisclosure, BiasDisclosureSeverity } from "@/types/workstation-6";

const severityBadgeVariant: Record<BiasDisclosureSeverity, "outline" | "warning" | "danger"> = {
  info: "outline",
  caution: "warning",
  warning: "danger"
};

const severityLabel: Record<BiasDisclosureSeverity, string> = {
  info: "Info",
  caution: "Caution",
  warning: "Warning"
};

function SeverityIcon({ severity }: { severity: BiasDisclosureSeverity }) {
  switch (severity) {
    case "warning": return <ShieldAlert className="h-4 w-4 text-danger" aria-hidden="true" />;
    case "caution": return <ShieldQuestion className="h-4 w-4 text-warning" aria-hidden="true" />;
    default:        return <ShieldCheck className="h-4 w-4 text-muted-foreground" aria-hidden="true" />;
  }
}

/**
 * Honest-assumptions panel rendered next to backtest results: fill timing, limit/stop realism,
 * universe provenance, corporate-action handling, and any detected data-quality issues that could
 * flatter the numbers. Simulated performance is never shown without its caveats.
 */
function BiasDisclosurePanelComponent({
  disclosure,
  className
}: {
  disclosure: BiasDisclosure | null | undefined;
  className?: string;
}) {
  if (!disclosure || disclosure.items.length === 0) {
    return null;
  }

  return (
    <section
      aria-label="Backtest bias disclosure"
      data-testid="bias-disclosure-panel"
      className={cn("rounded-md border border-border/70 bg-secondary/20 px-4 py-3", className)}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <SeverityIcon severity={disclosure.maxSeverity} />
          <span className="eyebrow-label">Bias disclosure</span>
        </div>
        <Badge variant={severityBadgeVariant[disclosure.maxSeverity]}>
          {severityLabel[disclosure.maxSeverity]}
        </Badge>
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">
        Simulation assumptions behind these numbers. Treat results as unproven until the caveats below are acceptable.
      </p>
      <ul className="mt-3 space-y-2">
        {disclosure.items.map((item) => (
          <li key={item.code} className="flex items-start gap-2" data-testid={`bias-disclosure-item-${item.code}`}>
            <div className="mt-0.5 shrink-0">
              <SeverityIcon severity={item.severity} />
            </div>
            <div className="min-w-0">
              <div className="text-sm font-medium text-foreground">{item.title}</div>
              <p className="text-xs leading-5 text-muted-foreground">{item.detail}</p>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

export const BiasDisclosurePanel = memo(BiasDisclosurePanelComponent);
