import { useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { apiGetJson } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { formatTaxDecimal as formatDecimalText } from "@/lib/ledger-tax-results-format";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { routeWithParam } from "@/lib/workstation-endpoints";
import { workstationRouteWithQuery } from "@/lib/workspace";

type SettlementLeg = "GaapTrade" | "GaapSettlement" | "CashSettlement";
const legLabel: Record<SettlementLeg, string> = {
  GaapTrade: "GAAP trade",
  GaapSettlement: "GAAP settlement",
  CashSettlement: "Cash-basis settlement"
};

interface BookFxSettlementSet {
  plan: {
    input: { setId: string; symbol: string; tradeDate: string; settlementDate: string; transactionCurrency: string };
    functionalCurrency: string;
    approvedBy: string;
    rateSetHashSha256: string;
    planHashSha256: string;
    amounts: {
      gaapTradeReceivable: string;
      gaapMarketResult: string;
      receivableRelieved: string;
      functionalCash: string;
      gaapOutstandingReceivable: string;
      cashMarketResult: string;
      settlementFxResult: string;
    };
  };
  receipts: { leg: SettlementLeg; journalEntryId: string; periodId: string }[];
  missingLegs: SettlementLeg[];
  invalidatedLegs: SettlementLeg[];
  isComplete: boolean;
}

export function BookFxSettlementPanel() {
  const id = useId();
  const [setId, setSetId] = useState("");
  const [result, setResult] = useState<BookFxSettlementSet | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);

  const load = async () => {
    if (!setId.trim() || active.current) return;
    const scope = setId.trim();
    const controller = new AbortController();
    active.current = controller;
    setError(null);
    setLoading(true);
    setResult(null);
    try {
      const response = await apiGetJson<BookFxSettlementSet>(
        routeWithParam(UI_API_ROUTES.LedgerBookFxSettlementSet, "setId", scope),
        { signal: controller.signal, allowDevelopmentFallback: false }
      );
      if (response?.plan?.input?.setId?.toLowerCase() !== scope.toLowerCase())
        throw new Error("The settlement evidence does not match the selected posting set.");
      if (!controller.signal.aborted) setResult(response);
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Settlement evidence is unavailable.").summary);
    } finally {
      active.current = null;
      if (!controller.signal.aborted) setLoading(false);
    }
  };

  return <section className="panel-surface space-y-4 p-4" aria-label="Book and FX settlement evidence">
    <h3 className="workspace-section-title">Book and FX settlement evidence</h3>
    <form className="flex flex-wrap items-end gap-3" onSubmit={event => { event.preventDefault(); void load(); }}>
      <FormRow label="Posting set ID" labelFor={id}>
        <Input id={id} value={setId} disabled={loading} onChange={event => { setSetId(event.currentTarget.value); setResult(null); setError(null); }} />
      </FormRow>
      <Button type="submit" variant="outline" disabled={loading || !setId.trim()}>{loading ? "Loading evidence…" : "Review posting set"}</Button>
    </form>
    {error ? <StatusBanner tone="danger" role="alert" title="Posting set unavailable" detail={error} /> : null}
    {result ? <SettlementEvidence result={result} /> : null}
  </section>;
}

function SettlementEvidence({ result }: { result: BookFxSettlementSet }) {
  const { plan } = result;
  const amounts = [
    { label: "Trade-date receivable", value: plan.amounts.gaapTradeReceivable },
    { label: "GAAP market result", value: plan.amounts.gaapMarketResult },
    { label: "Receivable relieved", value: plan.amounts.receivableRelieved },
    { label: "Cash settled", value: plan.amounts.functionalCash },
    { label: "Outstanding receivable", value: plan.amounts.gaapOutstandingReceivable },
    { label: "Cash-basis market result", value: plan.amounts.cashMarketResult },
    { label: "Settlement FX result", value: plan.amounts.settlementFxResult }
  ];
  return <div className="space-y-3">
    <StatusBanner tone={result.isComplete ? "success" : "warning"}
      title={result.isComplete ? "All required journal legs retained" : "Posting set incomplete"}
      detail={`${plan.input.symbol} · trade ${plan.input.tradeDate} · settlement ${plan.input.settlementDate} · ${plan.input.transactionCurrency} to ${plan.functionalCurrency}`} />
    <dl className="grid gap-2 sm:grid-cols-3">{amounts.map(amount => <div key={amount.label} className="rounded border border-border p-2">
      <dt className="text-sm text-muted-foreground">{amount.label}</dt>
      <dd className="font-mono text-sm">{formatDecimalText(amount.value)} {plan.functionalCurrency}</dd>
    </div>)}</dl>
    <ul className="space-y-1 text-sm">
      {result.missingLegs.map(leg => <li key={leg}>Missing {legLabel[leg]}</li>)}
      {result.invalidatedLegs.map(leg => <li key={leg}>Review required for {legLabel[leg]}</li>)}
      {result.receipts.map(receipt => <li key={receipt.leg}><Link className="underline" to={workstationRouteWithQuery("accountingJournalEntryDetail", {
        journalEntryId: receipt.journalEntryId, periodId: receipt.periodId
      })}>{legLabel[receipt.leg]} journal</Link></li>)}
    </ul>
    <div className="break-all text-xs text-muted-foreground">
      <p>Reviewed by {plan.approvedBy}</p>
      <p>Rate set hash <code>{plan.rateSetHashSha256}</code></p>
      <p>Plan hash <code>{plan.planHashSha256}</code></p>
    </div>
  </div>;
}
