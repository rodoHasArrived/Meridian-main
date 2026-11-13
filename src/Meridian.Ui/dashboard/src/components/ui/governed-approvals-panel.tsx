import { CheckCircle, PauseCircle, XCircle } from "lucide-react";

import { DetailFields, type DetailField } from "@/components/meridian/detail-pane";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { formatPercent, formatPrefixedCurrency, formatQuantity, formatUtcDate } from "@/lib/format";
import type { GovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";
import type { RiskEscalation, RiskEscalationOptionContract, RiskEscalationOrderLeg } from "@/types";

export interface GovernedApprovalsPanelProps {
  model: GovernedApprovalsViewModel;
}

// Prices, trail offsets and ratios keep the precision the order carries: an approver has to see
// the figure being authorized, not a rounded one.
const PRICE_FORMAT = { maxDecimals: 8 } as const;
const NOTIONAL_FORMAT = { minimumFractionDigits: 2, maximumFractionDigits: 8 } as const;
const MISSING = "—";

// Server enum names in operator wording. A name outside these maps is shown as the server sent it.
const TIME_IN_FORCE_LABELS = new Map([
  ["Day", "Day"],
  ["GoodTilCancelled", "Good til cancelled"],
  ["ImmediateOrCancel", "Immediate or cancel"],
  ["FillOrKill", "Fill or kill"]
]);
const POSITION_INTENT_LABELS = new Map([
  ["BuyToOpen", "Buy to open"],
  ["BuyToClose", "Buy to close"],
  ["SellToOpen", "Sell to open"],
  ["SellToClose", "Sell to close"]
]);
const OPTION_RIGHT_LABELS = new Map([
  ["C", "Call"],
  ["P", "Put"]
]);
// A leg's enums arrive as ordinals (see RiskEscalationOrderLeg), in server declaration order.
const LEG_SIDES = ["Buy", "Sell"];
const LEG_POSITION_INTENTS = ["BuyToOpen", "BuyToClose", "SellToOpen", "SellToClose"];

function formatNotional(value: number): string {
  return formatPrefixedCurrency(value, NOTIONAL_FORMAT);
}

function describeOrder(escalation: RiskEscalation): string {
  const price = escalation.limitPrice !== null ? ` @ ${escalation.limitPrice}` : "";
  // A dollar-sized order is routed by its notional, not by a share quantity, so the quantity
  // field (which can be a placeholder 1) must not be what the approver reads as the order's size.
  const size = escalation.routedNotional != null
    ? `${formatNotional(escalation.routedNotional)} notional of ${escalation.symbol}`
    : `${escalation.quantity} ${escalation.symbol}`;
  return `${escalation.side} ${size} ${escalation.type.toLowerCase()}${price}`;
}

function formatExpiration(value: string): string {
  const date = /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00Z`) : null;
  return date && !Number.isNaN(date.getTime()) ? formatUtcDate(date) : value;
}

function joinPresent(parts: Array<string | null | undefined>): string {
  return parts.filter((part): part is string => Boolean(part?.trim())).join(" · ");
}

/** The contract terms the server supplied; empty when it supplied none. */
function describeOptionContract(contract: RiskEscalationOptionContract | null | undefined): string {
  if (!contract) {
    return "";
  }

  return joinPresent([
    contract.underlyingSymbol,
    contract.expirationDate ? `Expires ${formatExpiration(contract.expirationDate)}` : null,
    contract.strikePrice != null ? `Strike ${formatQuantity(contract.strikePrice, PRICE_FORMAT)}` : null,
    contract.right ? OPTION_RIGHT_LABELS.get(contract.right) ?? contract.right : null,
    contract.multiplier ? `Multiplier ${contract.multiplier}` : null,
    contract.contractMonth ? `Contract month ${contract.contractMonth}` : null,
    contract.tradingClass ? `Trading class ${contract.tradingClass}` : null,
    contract.localSymbol ? `Local symbol ${contract.localSymbol}` : null
  ]);
}

function describeLeg(leg: RiskEscalationOrderLeg): string {
  // An ordinal outside the server enum is shown as missing rather than mapped to a real side.
  const side = LEG_SIDES[leg.side] ?? MISSING;
  const intent = leg.positionIntent != null ? LEG_POSITION_INTENTS[leg.positionIntent] : undefined;
  return joinPresent([
    `${side} ${leg.symbol}`,
    `Ratio ${formatQuantity(leg.ratioQuantity, PRICE_FORMAT)}`,
    intent ? POSITION_INTENT_LABELS.get(intent) : null,
    describeOptionContract(leg.optionContract)
  ]);
}

/**
 * The economic terms of a parked order beyond its headline. Only a term the server sent gets a
 * row: an empty value is dropped by the detail list, so a missing term is never shown as a zero
 * or a default. An opaque identifier (the fund account) goes behind the technical disclosure.
 */
function economicDetailFields(escalation: RiskEscalation): DetailField[] {
  const { timeInForce, positionIntent } = escalation;
  return [
    {
      label: "Routed notional",
      value: escalation.routedNotional != null ? formatNotional(escalation.routedNotional) : "",
      kind: "number",
      detail: "This order is sized in dollars, not in quantity."
    },
    { label: "Stop price", value: escalation.stopPrice != null ? formatQuantity(escalation.stopPrice, PRICE_FORMAT) : "", kind: "number" },
    { label: "Trail amount", value: escalation.trailPrice != null ? formatQuantity(escalation.trailPrice, PRICE_FORMAT) : "", kind: "number" },
    {
      label: "Trail percent",
      value: escalation.trailPercent != null ? formatPercent(escalation.trailPercent, { maximumFractionDigits: 8 }) : "",
      kind: "number"
    },
    { label: "Time in force", value: timeInForce ? TIME_IN_FORCE_LABELS.get(timeInForce) ?? timeInForce : "", kind: "text" },
    { label: "Position intent", value: positionIntent ? POSITION_INTENT_LABELS.get(positionIntent) ?? positionIntent : "", kind: "text" },
    { label: "Option contract", value: describeOptionContract(escalation.optionContract), kind: "text" },
    ...(escalation.legs ?? []).map((leg, index): DetailField => ({ label: `Leg ${index + 1}`, value: describeLeg(leg), kind: "text" })),
    { label: "Strategy", value: escalation.strategyId ?? "" },
    { label: "Fund account ID", value: escalation.fundAccountId ?? "", kind: "id" }
  ];
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
  const { escalations, forbidden, reasons, pendingId, errorText, statusText } = model;
  // Roles with trade-read but not order management cannot act on this queue at all;
  // showing them a permanently failing panel would be noise, not information.
  if (forbidden) {
    return null;
  }

  // Approved is actionable too: when a release is refused by a later gate the server
  // restores the entry to Approved so it can be retried once the blocking condition
  // clears. Dropping it here would leave the operator the refusal and no way to act on it.
  const pending = escalations.filter(
    (entry) => entry.status === "PendingApproval" || entry.status === "Approved");

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
            : `${pending.length} order(s) awaiting a risk decision or release. An order routes only once approved.`}
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
              <DetailFields
                fields={economicDetailFields(escalation)}
                label={`Order details for ${escalation.symbol} escalation`}
                columns="auto"
              />
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
                  aria-label={escalation.status === "Approved"
                    ? `Retry the release of the ${escalation.symbol} order`
                    : `Approve and release the ${escalation.symbol} order`}
                  onClick={() => void model.approve(escalation.escalationId)}
                >
                  <CheckCircle className="mr-2 h-4 w-4" />
                  {escalation.status === "Approved" ? "Retry release" : "Approve and release"}
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
