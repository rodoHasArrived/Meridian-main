import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { getAssetMarketValuation } from "@/lib/api/asset-market-valuation.api";
import { formatTaxDecimal as formatExactDecimal } from "@/lib/ledger-tax-results-format";
import type { AssetMarketValuationQuery } from "@/lib/workstation-endpoints";
import type { AssetPositionMarketValuation } from "@/types/asset-market-valuation";

export function AssetMarketValuation({
  securityId,
  query,
  currency
}: {
  securityId: string;
  query: AssetMarketValuationQuery;
  currency: string;
}) {
  const { positionId, ledgerBookId, accountId, valuationDate, economicStateId, economicStateVersion, knownAt } = query;
  const key = JSON.stringify([
    securityId,
    query.positionId,
    query.ledgerBookId,
    query.accountId,
    query.valuationDate,
    query.economicStateId,
    query.economicStateVersion,
    query.knownAt,
    currency
  ]);
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<{
    key: string;
    status: "loading" | "error" | "ready";
    result?: AssetPositionMarketValuation;
  }>({ key, status: "loading" });
  useEffect(() => {
    const controller = new AbortController();
    setState({ key, status: "loading" });
    void getAssetMarketValuation(
      securityId,
      {
        positionId,
        ledgerBookId,
        accountId,
        valuationDate,
        economicStateId,
        economicStateVersion,
        ...(knownAt ? { knownAt } : {})
      },
      currency,
      { signal: controller.signal }
    ).then(
      (result) => {
        if (!controller.signal.aborted) setState({ key, status: "ready", result });
      },
      () => {
        if (!controller.signal.aborted) setState({ key, status: "error" });
      }
    );
    return () => controller.abort();
  }, [
    key,
    attempt,
    securityId,
    positionId,
    ledgerBookId,
    accountId,
    valuationDate,
    economicStateId,
    economicStateVersion,
    knownAt,
    currency
  ]);
  const current = state.key === key ? state : null;
  return (
    <section aria-label="Market valuation" className="space-y-2 border-t border-border pt-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-xs font-semibold">Market valuation · {query.valuationDate}</h4>
        {current?.status === "ready" ? (
          <Badge variant={current.result?.status === "Ready" ? "outline" : "warning"}>{current.result?.status}</Badge>
        ) : null}
      </div>
      {!current || current.status === "loading" ? (
        <p role="status" className="text-xs text-muted-foreground">
          Valuing the retained position…
        </p>
      ) : current.status === "error" || !current.result ? (
        <div role="alert" className="space-y-2 text-sm">
          <p>Couldn’t load a matching market valuation. Amounts are withheld.</p>
          <Button size="sm" variant="outline" onClick={() => setAttempt((value) => value + 1)}>
            Retry valuation
          </Button>
        </div>
      ) : (
        <>
          <dl className="grid gap-3 sm:grid-cols-2">
            <Value label={`Market value · ${currency}`} value={current.result.marketValue} />
            <Value label={`Unrealized mark-to-carrying P/L · ${currency}`} value={current.result.unrealizedPnl} />
          </dl>
          <p className="text-xs text-muted-foreground">{current.result.unrealizedPnlBasis}</p>
          {current.result.blockers.length ? (
            <ul className="space-y-1 text-xs text-warning" aria-label="Valuation blockers">
              {current.result.blockers.map((blocker, index) => (
                <li key={`${blocker.code}-${index}`}>
                  <span className="font-semibold">{blocker.code}:</span> {blocker.reason}
                </li>
              ))}
            </ul>
          ) : null}
          <p className="break-words text-xs text-muted-foreground">
            Source {current.result.selectedSource ?? "see model inputs"} · Observed{" "}
            {current.result.observedAsOf ?? "unavailable"} · {current.result.freshnessStatus}
          </p>
          <details className="text-xs">
            <summary className="cursor-pointer font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
              Valuation calculation and source evidence
            </summary>
            <div className="mt-2 space-y-2 break-words">
              <p>{current.result.calculationBasis}</p>
              <dl className="grid gap-2 sm:grid-cols-2">
                <Text label="Model" value={`${current.result.modelKey} · ${current.result.modelVersion}`} />
                <Text label="Price unit" value={current.result.priceUnit} />
                <Text
                  label="Price"
                  value={current.result.price === null ? "Unavailable" : formatExactDecimal(current.result.price)}
                />
                <Text label="Knowledge cutoff" value={current.result.knowledgeAsOf ?? "Unavailable"} />
                <Text label="Price selection receipt" value={current.result.selectionReceiptId ?? "Not supplied"} />
                <Text
                  label="Retained state"
                  value={`${current.result.economicStateId} · Version ${current.result.economicStateVersion}`}
                />
              </dl>
              <ul className="space-y-1" aria-label="Valuation inputs">
                {current.result.inputs.map((input, index) => (
                  <li key={index}>
                    {input.kind}: {input.reference}
                    {input.version != null ? ` · Version ${input.version}` : ""}
                    {input.asOf ? ` · As of ${input.asOf}` : ""}
                    {input.contentSha256 ? ` · SHA-256 ${input.contentSha256}` : ""}
                  </li>
                ))}
              </ul>
              {current.result.diagnostics.map((diagnostic, index) => (
                <p key={index}>{diagnostic}</p>
              ))}
            </div>
          </details>
        </>
      )}
    </section>
  );
}
function Value({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-sm font-semibold tabular-nums">
        {value === null ? "Unavailable" : formatExactDecimal(value)}
      </dd>
    </div>
  );
}
function Text({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-mono">{value}</dd>
    </div>
  );
}
