import { CheckCircle, PauseCircle, XCircle } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import type { GovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";
import type { RiskEscalation } from "@/types";

export interface GovernedApprovalsPanelProps {
  model: GovernedApprovalsViewModel;
}

function describeOrder(escalation: RiskEscalation): string {
  const price = escalation.limitPrice !== null ? ` @ ${escalation.limitPrice}` : "";
  return `${escalation.side} ${escalation.quantity} ${escalation.symbol} ${escalation.type.toLowerCase()}${price}`;
}

/**
 * Operator surface for the governed-approval queue. An order parked by a risk escalation
 * cannot route until someone approves it here, so without this panel the browser could
 * park orders it had no way to resolve.
 *
 * The server enforces the rules this UI only reflects: the submitting operator cannot
 * approve their own escalation, and both actions re-check the caller's fund-account scope
 * against the retained order. A refusal comes back as an error rather than being predicted
 * client-side.
 */
export function GovernedApprovalsPanel({ model }: GovernedApprovalsPanelProps) {
  const { escalations, reasons, pendingId, errorText, statusText } = model;
  const pending = escalations.filter((entry) => entry.status === "PendingApproval");

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <PauseCircle className="h-4 w-4" />
          Governed approvals
        </CardTitle>
        <CardDescription>
          {pending.length === 0
            ? "No orders are awaiting a risk decision."
            : `${pending.length} order(s) parked awaiting a risk decision. An order routes only once approved.`}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {errorText && (
          <div role="alert" className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            {errorText}
          </div>
        )}
        {statusText && (
          <div role="status" className="rounded-lg border border-success/30 bg-success/10 px-4 py-3 text-sm text-success">
            {statusText}
          </div>
        )}
        {pending.map((escalation) => {
          const busy = pendingId === escalation.escalationId;
          const reason = reasons[escalation.escalationId] ?? "";
          return (
            <div
              key={escalation.escalationId}
              className="rounded-lg border border-border/60 px-4 py-3 space-y-2"
              data-testid="governed-approval-entry"
            >
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="font-medium">{describeOrder(escalation)}</span>
                <span className="text-xs text-muted-foreground">{escalation.ruleName ?? "risk"}</span>
              </div>
              <p className="text-sm text-muted-foreground">{escalation.reason}</p>
              <Input
                aria-label={`Decision reason for ${escalation.symbol} escalation`}
                placeholder="Reason for the decision (recorded in the audit trail)"
                value={reason}
                disabled={busy}
                onChange={(event) => model.setReason(escalation.escalationId, event.target.value)}
              />
              <div className="flex gap-2">
                <Button
                  type="button"
                  size="sm"
                  disabled={busy || reason.trim().length === 0}
                  aria-label={`Approve and release the ${escalation.symbol} order`}
                  onClick={() => void model.approve(escalation.escalationId)}
                >
                  <CheckCircle className="mr-2 h-4 w-4" />
                  Approve and release
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={busy || reason.trim().length === 0}
                  aria-label={`Deny the ${escalation.symbol} order`}
                  onClick={() => void model.deny(escalation.escalationId)}
                >
                  <XCircle className="mr-2 h-4 w-4" />
                  Deny
                </Button>
              </div>
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
