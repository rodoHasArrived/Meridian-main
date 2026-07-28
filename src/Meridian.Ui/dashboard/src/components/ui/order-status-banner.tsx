import { AlertTriangle, CheckCircle, PauseCircle } from "lucide-react";

import { CardContent } from "@/components/ui/card";

export interface OrderStatusBannerProps {
  /** Confirmation text for an order the broker accepted. */
  successText: string | null;
  /**
   * Text for an order parked awaiting governed risk approval. Nothing routed, but a live
   * queue entry can still execute it — so this is deliberately not styled as a failure.
   */
  parkedText: string | null;
  /**
   * Non-blocking warnings the risk rails raised while approving. The order routed, so this
   * is not a failure — but the warnings describe exposure the operator now holds and must
   * not be swallowed by an unqualified success banner.
   */
  riskWarnings?: string[];
}

/**
 * Post-submission banner for the order ticket. Parked takes precedence over submitted:
 * only one of the two can be live at a time, and a park is the more recent outcome.
 */
export function OrderStatusBanner({ successText, parkedText, riskWarnings = [] }: OrderStatusBannerProps) {
  if (!successText && !parkedText) {
    return null;
  }

  const parked = parkedText !== null;
  return (
    <CardContent className="border-b border-border/60 pb-4 space-y-2">
      <div
        role="status"
        className={parked
          ? "rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning flex items-center gap-2"
          : "rounded-lg border border-success/30 bg-success/10 px-4 py-3 text-sm text-success flex items-center gap-2"}
      >
        {parked ? <PauseCircle className="h-4 w-4 shrink-0" /> : <CheckCircle className="h-4 w-4 shrink-0" />}
        {parkedText ?? successText}
      </div>
      {riskWarnings.length > 0 && (
        <div className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning flex items-start gap-2">
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <ul className="space-y-1">
            {riskWarnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
        </div>
      )}
    </CardContent>
  );
}
