/**
 * Banking payment-approval queue for Accounting → Approvals.
 *
 * `/api/banking/payments/*` carries a full initiate → review → evidence workflow
 * and had no operator surface at all: pending intents could be created but never
 * seen or decided from the workstation. This panel is the review half of that
 * workflow, alongside the bank evidence recorded against it.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
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

export function BankingPaymentApprovalsPanel() {
  const [payments, setPayments] = useState<PendingPayment[]>([]);
  const [transactions, setTransactions] = useState<BankTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [decision, setDecision] = useState<{ id: string; kind: DecisionKind } | null>(null);
  const [decisionText, setDecisionText] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    const [paymentResult, transactionResult] = await Promise.allSettled([
      getPendingPayments(),
      getBankTransactions()
    ]);

    setPayments(paymentResult.status === "fulfilled" ? paymentResult.value : []);
    setTransactions(transactionResult.status === "fulfilled" ? transactionResult.value : []);
    if (paymentResult.status === "rejected") {
      setError(errorMessage(paymentResult.reason));
    }
    setLoading(false);
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const rows = useMemo(() => payments.map(buildPendingPaymentRow), [payments]);
  const summary = useMemo(() => buildPaymentQueueSummary(payments, transactions), [payments, transactions]);

  async function submitDecision() {
    if (!decision) {
      return;
    }

    const reason = decisionText.trim();
    // The server requires a reason to reject; sending an empty one would record a
    // decision with no rationale behind it.
    if (decision.kind === "reject" && !reason) {
      setError("A rejection needs a reason; it is retained with the decision.");
      return;
    }

    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = decision.kind === "approve"
        ? await approvePendingPayment(decision.id, { reviewNotes: reason || null, actionOrigin: "HumanOperator" })
        : await rejectPendingPayment(decision.id, { reason, actionOrigin: "HumanOperator" });
      setNotice(`Payment ${result.pendingPaymentId} is now ${buildPendingPaymentRow(result).statusLabel}.`);
      setDecision(null);
      setDecisionText("");
      await refresh();
    } catch (reason_) {
      setError(errorMessage(reason_));
    } finally {
      setBusy(false);
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
          <Button size="sm" variant="outline" onClick={() => void refresh()}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
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
                  {loading ? "Loading payment intents…" : summary.emptyMessage}
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
