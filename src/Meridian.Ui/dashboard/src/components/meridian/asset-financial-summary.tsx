import { useEffect, useId, useState } from "react";
import { AssetMarketValuation } from "./asset-market-valuation";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { getAssetOperations, getLiveQuotesSnapshot } from "@/lib/api";
import { workstationRouteWithQuery } from "@/lib/workspace";
import type { AssetOperationsDetail, BookPosition, QuotesSnapshotItem } from "@/types";

interface AssetFinancialSummaryProps {
  securityId: string;
  symbol: string;
  currency: string;
  identifierKind?: string | null;
  ledgerBookId?: string | null;
  accountId?: string | null;
  asOf?: string | null;
  scopeUnavailableReason?: string | null;
}

type Resource<T> =
  { key: string; status: "loading" } | { key: string; status: "ready"; data: T } | { key: string; status: "error" };

const numberFormat = new Intl.NumberFormat("en-US", {
  maximumFractionDigits: 6
});
const valueText = (value: number | null | undefined) =>
  value != null && Number.isFinite(value) ? numberFormat.format(value) : "Unavailable";

/** Retained book-state amounts are never reconstructed from symbol quotes or local lots. */
export function AssetFinancialSummary({
  securityId,
  symbol,
  currency,
  identifierKind,
  ledgerBookId,
  accountId,
  asOf,
  scopeUnavailableReason
}: AssetFinancialSummaryProps) {
  const selectorId = useId();
  const [holdings, setHoldings] = useState<Resource<AssetOperationsDetail>>({
    key: securityId,
    status: "loading"
  });
  const [quote, setQuote] = useState<Resource<QuotesSnapshotItem | null>>({
    key: symbol,
    status: "loading"
  });
  const [holdingsAttempt, retryHoldings] = useState(0);
  const [quoteAttempt, retryQuote] = useState(0);
  const [selection, setSelection] = useState<{
    scope: string;
    positionId: string;
  } | null>(null);
  const scopeKey = JSON.stringify([securityId, currency, ledgerBookId, accountId, asOf]);
  const quoteKey = JSON.stringify([securityId, symbol, identifierKind]);
  const supportsQuote = identifierKind?.toLowerCase() === "ticker";

  useEffect(() => {
    const controller = new AbortController();
    setHoldings({ key: securityId, status: "loading" });
    getAssetOperations(securityId, {
      signal: controller.signal,
      allowDevelopmentFallback: false
    })
      .then((data) => {
        if (controller.signal.aborted) return;
        const positionIds = (data.bookPositions ?? []).map((position) => position.positionId);
        if (
          data.subject.securityId !== securityId ||
          new Set(positionIds).size !== positionIds.length ||
          data.bookPositions?.some(
            (position) =>
              position.securityId !== securityId ||
              (position.currentEconomicState && position.currentEconomicState.positionId !== position.positionId)
          )
        ) {
          throw new Error("Asset operations identity mismatch.");
        }
        setHoldings({ key: securityId, status: "ready", data });
      })
      .catch(() => {
        if (!controller.signal.aborted) setHoldings({ key: securityId, status: "error" });
      });
    return () => controller.abort();
  }, [securityId, holdingsAttempt]);

  useEffect(() => {
    const controller = new AbortController();
    setQuote({ key: quoteKey, status: "loading" });
    if (supportsQuote) {
      getLiveQuotesSnapshot([symbol], {
        signal: controller.signal,
        allowDevelopmentFallback: false
      })
        .then((snapshot) => {
          if (controller.signal.aborted) return;
          const matches = snapshot.quotes.filter(
            (item) => item.symbol.trim().toUpperCase() === symbol.trim().toUpperCase()
          );
          if (matches.length > 1) throw new Error("Ambiguous symbol quote.");
          setQuote({
            key: quoteKey,
            status: "ready",
            data: matches[0] ?? null
          });
        })
        .catch(() => {
          if (!controller.signal.aborted) setQuote({ key: quoteKey, status: "error" });
        });
    }
    return () => controller.abort();
  }, [quoteKey, quoteAttempt, supportsQuote, symbol]);

  const currentHoldings = holdings.key === securityId ? holdings : null;
  const currentQuote = quote.key === quoteKey && quote.status === "ready" ? quote.data : null;
  const positions =
    !scopeUnavailableReason && currentHoldings?.status === "ready"
      ? (currentHoldings.data.bookPositions ?? []).filter(
          (position) =>
            (!ledgerBookId || position.bookContext.ledgerBookId === ledgerBookId) &&
            (!accountId || position.primaryAccountId === accountId) &&
            (!asOf || position.currentEconomicState?.asOfDate === asOf)
        )
      : [];
  const selected =
    positions.find((position) => selection?.scope === scopeKey && position.positionId === selection.positionId) ??
    (positions.length === 1 ? positions[0] : undefined);
  const state = selected?.currentEconomicState;
  const validState =
    state?.currency === currency &&
    (!state.sourceEvent?.securityId || state.sourceEvent.securityId === securityId) &&
    (!state.sourceEvent?.bookPositionId || state.sourceEvent.bookPositionId === selected?.positionId)
      ? state
      : null;

  return (
    <section aria-label="Financial context" className="panel-surface space-y-3 px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">Financial context</h3>
        <Link
          className="text-xs text-primary underline-offset-2 hover:underline"
          to={workstationRouteWithQuery("portfolio", { symbol })}
        >
          Open portfolio
        </Link>
      </div>
      {scopeUnavailableReason ? (
        <p className="text-sm text-muted-foreground">{scopeUnavailableReason} Holdings amounts are withheld.</p>
      ) : currentHoldings?.status === "error" ? (
        <div role="status" className="flex flex-wrap items-center gap-2 text-sm">
          <span>Couldn’t load book positions.</span>
          <Button size="sm" variant="outline" onClick={() => retryHoldings((attempt) => attempt + 1)}>
            Retry book positions
          </Button>
        </div>
      ) : currentHoldings?.status !== "ready" ? (
        <p role="status" className="text-sm text-muted-foreground">
          Loading book positions…
        </p>
      ) : positions.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No retained book position matches this asset and scope. Holdings amounts are unavailable.
        </p>
      ) : (
        <>
          {positions.length > 1 ? (
            <div className="grid max-w-xl gap-1">
              <label htmlFor={selectorId} className="text-xs text-muted-foreground">
                Book position
              </label>
              <select
                id={selectorId}
                value={selected?.positionId ?? ""}
                onChange={(event) =>
                  setSelection({
                    scope: scopeKey,
                    positionId: event.target.value
                  })
                }
                className="min-h-9 rounded-[2px] border border-border bg-background px-2 text-sm focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
              >
                <option value="">Select a book position</option>
                {positions.map((position, index) => (
                  <option key={position.positionId} value={position.positionId}>
                    Position {index + 1} · {positionLabel(position)}
                  </option>
                ))}
              </select>
            </div>
          ) : null}
          {selected ? (
            <div className="space-y-2">
              <p className="break-words text-xs text-muted-foreground">
                {positionLabel(selected)} · {selected.positionSide} · {selected.status}
              </p>
              {validState ? (
                <>
                  <dl className="grid gap-3 sm:grid-cols-3">
                    <Metric label="Quantity" value={valueText(validState.quantity)} />
                    <Metric label={`Unit cost · ${validState.currency}`} value={valueText(validState.unitCost)} />
                    <Metric
                      label={`Carrying amount · ${validState.currency}`}
                      value={valueText(validState.carryingAmount)}
                    />
                  </dl>
                  <p className="break-words text-xs text-muted-foreground">
                    As of {validState.asOfDate} · Asset operations · Version {validState.version}
                    {validState.sourceEvent ? ` · Source ${validState.sourceEvent.sourceDomain}` : ""}
                  </p>
                </>
              ) : (
                <p className="text-sm text-muted-foreground">
                  A matching {currency} economic state is unavailable. Holdings amounts are withheld.
                </p>
              )}
              <TechnicalDetails label="Position and valuation identifiers">
                <dl className="grid gap-2 text-xs sm:grid-cols-2">
                  <div>
                    <dt className="text-muted-foreground">Book ID</dt>
                    <dd className="break-all font-mono">{selected.bookContext.ledgerBookId}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">Account ID</dt>
                    <dd className="break-all font-mono">{selected.primaryAccountId ?? "Not supplied"}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">Position ID</dt>
                    <dd className="break-all font-mono">{selected.positionId}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">Economic-state ID</dt>
                    <dd className="break-all font-mono">{validState?.economicStateId ?? "Unavailable"}</dd>
                  </div>
                </dl>
              </TechnicalDetails>
              {validState ? (
                selected.primaryAccountId ? (
                  <AssetMarketValuation
                    securityId={securityId}
                    currency={validState.currency}
                    query={{
                      positionId: selected.positionId,
                      ledgerBookId: selected.bookContext.ledgerBookId,
                      accountId: selected.primaryAccountId,
                      valuationDate: validState.asOfDate,
                      economicStateId: validState.economicStateId,
                      economicStateVersion: validState.version
                    }}
                  />
                ) : (
                  <p className="text-xs text-muted-foreground">
                    A retained financial-account identity is required for market valuation.
                  </p>
                )
              ) : null}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              Select one position to inspect its amounts. Positions are not aggregated across books or accounts.
            </p>
          )}
        </>
      )}
      <div className="border-t border-border pt-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h4 className="text-xs font-semibold">Symbol quote · {symbol}</h4>
          <Link
            className="text-xs text-primary underline-offset-2 hover:underline"
            to={workstationRouteWithQuery("dataQuotes", { symbol })}
          >
            Inspect quotes
          </Link>
        </div>
        {!supportsQuote ? (
          <p className="mt-1 text-xs text-muted-foreground">A ticker is required to look up a symbol quote.</p>
        ) : quote.key === quoteKey && quote.status === "error" ? (
          <div role="status" className="mt-1 flex flex-wrap items-center gap-2 text-sm">
            <span>Couldn’t load the symbol quote.</span>
            <Button size="sm" variant="outline" onClick={() => retryQuote((attempt) => attempt + 1)}>
              Retry quote
            </Button>
          </div>
        ) : quote.key !== quoteKey || quote.status === "loading" ? (
          <p role="status" className="mt-1 text-xs text-muted-foreground">
            Loading symbol quote…
          </p>
        ) : !currentQuote ? (
          <p className="mt-1 text-xs text-muted-foreground">No quote returned for this ticker.</p>
        ) : (
          <>
            <dl className="mt-2 grid gap-3 sm:grid-cols-3">
              <Metric label="Last trade" value={valueText(currentQuote.lastPrice)} />
              <Metric label="Bid" value={valueText(currentQuote.bidPrice)} />
              <Metric label="Ask" value={valueText(currentQuote.askPrice)} />
            </dl>
            <p className="mt-2 break-words text-xs text-muted-foreground">
              Quote observed {currentQuote.timestamp} · Last trade{" "}
              {currentQuote.lastTradeTimestamp ?? "time unavailable"} · Venue {currentQuote.venue ?? "unavailable"} ·
              Stream {currentQuote.streamId ?? "unavailable"}
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              Symbol-level market data; quote currency is not supplied. This quote is not a book valuation.
            </p>
          </>
        )}
      </div>
    </section>
  );
}

function positionLabel(position: BookPosition) {
  return `${position.bookContext.displayName} · ${position.bookContext.accountingBasis} · ${position.primaryAccountId ? "Account-scoped holding" : "Account unavailable"}`;
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-[0.1em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-mono text-sm font-semibold tabular-nums">{value}</dd>
    </div>
  );
}
