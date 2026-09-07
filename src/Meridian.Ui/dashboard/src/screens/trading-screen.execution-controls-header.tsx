import { OctagonX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { ExecutionEvidenceViewModel, TradingConfirmAction } from "@/screens/trading-screen.view-model";

/**
 * Title row of the execution controls snapshot: the kill switch, the refresh control, and the
 * breaker state readout. The breaker button is absent rather than disabled when no snapshot has
 * loaded - an execution control the operator cannot act on should not look like one they can.
 */
export function ExecutionControlsHeader({
  executionEvidence,
  onConfirm
}: {
  executionEvidence: ExecutionEvidenceViewModel;
  onConfirm: (action: TradingConfirmAction) => void;
}) {
  const panel = executionEvidence.controlsPanel;

  return (
    <div className="mb-2 flex items-center justify-between gap-3">
      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {panel?.title ?? "Execution controls snapshot"}
      </p>
      <div className="panel-action-zone">
        {panel && (
          <Button
            size="sm"
            variant={panel.breakerAction.kind === "open-circuit-breaker" ? "destructive" : "outline"}
            onClick={() => onConfirm(panel.breakerAction)}
            disabled={panel.breakerActionDisabled}
            disabledReason={panel.breakerActionDisabledReason}
            aria-label={panel.breakerActionAriaLabel}
            title={panel.breakerActionAriaLabel}
          >
            <OctagonX className="mr-2 h-4 w-4" />
            {panel.breakerActionLabel}
          </Button>
        )}
        <Button
          size="sm"
          variant="outline"
          onClick={() => { void executionEvidence.refresh(); }}
          disabled={executionEvidence.refreshDisabled}
          disabledReason={executionEvidence.refreshDisabledReason}
          busy={executionEvidence.loading}
          busyLabel={executionEvidence.refreshBusyLabel}
          aria-label={executionEvidence.refreshAriaLabel}
        >
          {executionEvidence.refreshButtonLabel}
        </Button>
        <span
          className={cn(
            "text-xs font-semibold uppercase tracking-[0.14em]",
            panel?.statusTone === "danger" ? "text-danger" : "text-success"
          )}
        >
          {panel?.statusLabel ?? "Snapshot unavailable"}
        </span>
      </div>
    </div>
  );
}
