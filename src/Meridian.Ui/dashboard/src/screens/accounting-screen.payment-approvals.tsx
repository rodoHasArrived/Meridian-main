/**
 * Banking payment-approval queue for Accounting → Approvals.
 *
 * `/api/banking/payments/*` carries a full initiate → review → evidence workflow
 * and had no operator surface at all: pending intents could be created but never
 * seen or decided from the workstation. This panel is the review half of that
 * workflow, alongside the bank evidence recorded against it.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Landmark, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import {
  approvePendingPayment,
  getBankTransactions,
  getPendingPayments,
  rejectPendingPayment
} from "@/lib/api/banking-payments.api";
import {
  buildPaymentQueueSummary,
  buildPendingPaymentRow,
  type PaymentApprovalTone
} from "@/screens/accounting-screen.payment-approvals.view-model";
import type { BankTransaction, PendingPayment } from "@/types/banking-payments.types";

type DecisionKind = "approve" | "reject";

export function BankingPaymentApprovalsPanel({ entityId }: { entityId?: string | null }) {
  const scope = entityId?.trim() || undefined;
  return <PaymentEntityScope key={scope ?? "unselected"} initialEntityId={scope} />;
}

function PaymentEntityScope({ initialEntityId }: { initialEntityId?: string }) {
  const [scope, setScope] = useState(initialEntityId);
  const [draft, setDraft] = useState(initialEntityId ?? "");
  const unapplied = (draft.trim() || undefined) !== scope;
  // A scope change starts a new review session, including pending decisions.
  return <div className="space-y-3">
    <form className="flex flex-wrap items-end gap-2" onSubmit={(event) => {
      event.preventDefault();
      setScope(draft.trim() || undefined);
    }}>
      <label className="text-sm font-medium">Payment entity ID
        <Input className="mt-1" value={draft} onChange={(event) => setDraft(event.target.value)}
          placeholder="Enter the entity to review" />
      </label>
      <Button type="submit" variant="outline" disabled={!unapplied}>Use entity</Button>
      <span className="text-xs text-muted-foreground" role="status">
        {scope ? `Reviewing entity ${scope}.` : "No entity selected."}
        {unapplied ? " Apply the entity selection before reviewing payments." : ""}
      </span>
    </form>
    <fieldset disabled={unapplied} className="min-w-0">
      <legend className="sr-only">{scope ? `Payments for entity ${scope}` : "No payment entity selected"}</legend>
      <EntityPaymentApprovalsPanel key={scope ?? "unselected"} entityId={scope} />
    </fieldset>
  </div>;
}

function EntityPaymentApprovalsPanel({ entityId }: { entityId?: string }) {
  const [payments, setPayments] = useState<PendingPayment[]>([]);
  const [transactions, setTransactions] = useState<BankTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [decision, setDecision] = useState<{ id: string; kind: DecisionKind } | null>(null);
  const [decisionText, setDecisionText] = useState("");
  const [busy, setBusy] = useState(false);
  const readRequest = useRef<AbortController | null>(null);
  const decisionRequest = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    if (!entityId) {
      setLoading(false);
      return;
    }
    readRequest.current?.abort();
    const controller = new AbortController();
    readRequest.current = controller;
    setLoading(true);
    setError(null);
    const options = { signal: controller.signal };
    const [paymentResult, transactionResult] = await Promise.allSettled([
      getPendingPayments(entityId, options),
      getBankTransactions(entityId, options)
    ]);
    if (controller.signal.aborted) return;

    setPayments(paymentResult.status === "fulfilled" ? paymentResult.value : []);
    setTransactions(transactionResult.status === "fulfilled" ? transactionResult.value : []);
    if (paymentResult.status === "rejected") {
      setError(errorMessage(paymentResult.reason));
    }
    setLoading(false);
  }, [entityId]);

  useEffect(() => {
    void refresh();
    return () => {
      readRequest.current?.abort();
      decisionRequest.current?.abort();
    };
  }, [refresh]);

  const rows = useMemo(() => payments.map(buildPendingPaymentRow), [payments]);
  const summary = useMemo(() => buildPaymentQueueSummary(payments, transactions), [payments, transactions]);

  async function submitDecision() {
    if (!decision || !entityId || busy) {
      return;
    }

    const reason = decisionText.trim();
    // The server requires a reason to reject; sending an empty one would record a
    // decision with no rationale behind it.
    if (decision.kind === "reject" && !reason) {
      setError("A rejection needs a reason; it is retained with the decision.");
      return;
    }

    const controller = new AbortController();
    decisionRequest.current = controller;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = decision.kind === "approve"
        ? await approvePendingPayment(decision.id, entityId, { reviewNotes: reason || null, actionOrigin: "HumanOperator" }, { signal: controller.signal })
        : await rejectPendingPayment(decision.id, entityId, { reason, actionOrigin: "HumanOperator" }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setNotice(`Payment ${result.pendingPaymentId} is now ${buildPendingPaymentRow(result).statusLabel}.`);
      setDecision(null);
      setDecisionText("");
      await refresh();
    } catch (reason_) {
      if (!controller.signal.aborted) setError(errorMessage(reason_));
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Banking</div>
            <CardTitle className="flex items-center gap-2">
              <Landmark className="h-5 w-5 text-primary" />
              Payment approvals
            </CardTitle>
            <CardDescription>
              Payment intents awaiting review, with the bank evidence already recorded against them.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" disabled={!entityId || loading || busy} onClick={() => void refresh()}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {!entityId ? <StatusBanner tone="warning" title="Select an entity to review payments" detail="Payment decisions and bank evidence belong to the selected entity." /> : null}
        {error ? <StatusBanner tone="danger" title="Payment queue needs attention" detail={error} /> : null}
        {notice ? <StatusBanner tone="success" title="Payment decision recorded" detail={notice} /> : null}
        {summary.unremediatedCount > 0 ? (
          <StatusBanner
            tone="warning"
            title={`${summary.unremediatedCount} payment intent(s) have no retained currency`}
            detail="The server refuses bank evidence and transfer authorization on these until the currency is repaired."
          />
        ) : null}

        <div className="grid gap-2 md:grid-cols-3" aria-label="Payment approval posture">
          <Metric label="Awaiting review" value={String(summary.pendingCount)} />
          <Metric label="Pending value" value={summary.pendingValueLabel} />
          <Metric label="Evidence recorded" value={String(summary.evidenceCount)} />
        </div>

        <table className="w-full text-sm" aria-label="Payment intents">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="py-2">Amount</th>
              <th>Effective</th>
              <th>Reference</th>
              <th>Status</th>
              <th className="sr-only">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={5} className="py-4 text-center text-muted-foreground">
                  {!entityId ? "Select an entity to load payment intents." : loading ? "Loading payment intents…" : summary.emptyMessage}
                </td>
              </tr>
            ) : rows.map((row) => (
              <tr key={row.pendingPaymentId} className="border-t border-border/60" aria-label={row.ariaLabel}>
                <td className="py-2">
                  <div className="font-mono text-xs">{row.amount}</div>
                  <div className={row.currencyMissing ? "text-xs text-warning" : "text-xs text-muted-foreground"}>
                    {row.currencyLabel}
                  </div>
                </td>
                <td className="font-mono text-xs">{row.effectiveDate}</td>
                <td className="max-w-[16rem] break-words text-xs text-muted-foreground">{row.externalRef}</td>
                <td>
                  <Badge variant={badgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  {row.decisionSummary ? (
                    <div className="mt-1 text-xs text-muted-foreground">{row.decisionSummary}</div>
                  ) : null}
                </td>
                <td className="text-right">
                  {row.canDecide ? (
                    decision?.id === row.pendingPaymentId ? (
                      <span className="flex flex-wrap items-center justify-end gap-2">
                        <Input
                          aria-label={decision.kind === "approve" ? "Approval notes" : "Rejection reason"}
                          className="h-8 w-48"
                          value={decisionText}
                          onChange={(event) => setDecisionText(event.target.value)}
                        />
                        <Button size="sm" disabled={busy} onClick={() => void submitDecision()}>
                          Confirm {decision.kind}
                        </Button>
                        <Button size="sm" variant="outline" disabled={busy} onClick={() => setDecision(null)}>
                          Cancel
                        </Button>
                      </span>
                    ) : (
                      <span className="flex items-center justify-end gap-2">
                        <Button
                          size="sm"
                          onClick={() => { setDecision({ id: row.pendingPaymentId, kind: "approve" }); setDecisionText(""); }}
                        >
                          Approve
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => { setDecision({ id: row.pendingPaymentId, kind: "reject" }); setDecisionText(""); }}
                        >
                          Reject
                        </Button>
                      </span>
                    )
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-[2px] border border-border bg-secondary/20 p-3">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 text-xl font-semibold">{value}</div>
    </div>
  );
}

function badgeVariant(tone: PaymentApprovalTone): "default" | "success" | "warning" | "danger" {
  return tone === "default" ? "default" : tone;
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "The operation could not be completed.";
}
