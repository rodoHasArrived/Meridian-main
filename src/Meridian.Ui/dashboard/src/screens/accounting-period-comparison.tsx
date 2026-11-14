import { useEffect, useId, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { formatTaxDecimal } from "@/lib/ledger-tax-results-format";
import { getPostedPeriodComparison, type PostedComparisonQuery, type PostedPeriodComparison } from "@/lib/posted-period-comparison-api";
import type { ApiRequestOptions } from "@/lib/api";

type ComparisonService = (query: PostedComparisonQuery, options?: ApiRequestOptions) => Promise<PostedPeriodComparison>;
const dimensionKeys = new Set(["fundId", "fundProfileId", "entityId", "sleeveId", "strategyId", "investorId", "capitalAccountId", "instrumentId", "positionId", "taxLotId", "costCenterId", "counterpartyId", "organizationId", "portfolioId", "bookId", "ledgerBookDimensionId", "accountId", "customerId", "vendorId", "projectId", "externalGlDimensionKey", "externalGlDimensionValue"]);

function accountingDecimal(value: string): string {
  const formatted = formatTaxDecimal(value, 2);
  return formatted.startsWith("-") ? `(${formatted.slice(1)})` : formatted;
}

/** This panel only compares the immutable posted book; it never accepts strategy-run totals. */
export function AccountingPeriodComparison({ bookId, periodId, expectedBasis, enabled = true, periodOptions, service = getPostedPeriodComparison }: {
  bookId: string | null | undefined;
  periodId: string | null;
  expectedBasis?: string;
  enabled?: boolean;
  periodOptions: readonly { id: string; label: string; statusLabel: string }[];
  service?: ComparisonService;
}) {
  const id = useId();
  const [search, setSearch] = useSearchParams();
  const show = search.get("comparison") === "prior";
  const baselinePeriodId = search.get("baselinePeriodId") ?? "";
  const asOf = search.get("asOf") ?? undefined;
  const searchKey = search.toString();
  const dimensions = useMemo(() => Object.fromEntries([...new URLSearchParams(searchKey)].filter(([key, value]) => value && (dimensionKeys.has(key) || key.startsWith("externalGl.") || key.startsWith("dimension")))), [searchKey]);
  const incompleteExternalScope = Boolean(search.get("externalGlDimensionKey")?.trim()) !== Boolean(search.get("externalGlDimensionValue")?.trim());
  const unsupportedScope = search.has("shareClassId") || incompleteExternalScope;
  const key = JSON.stringify({ bookId, periodId, baselinePeriodId, asOf, dimensions, expectedBasis, enabled, show });
  const [state, setState] = useState<{ key: string; result?: PostedPeriodComparison; error?: string }>({ key: "" });
  const [reload, setReload] = useState(0);
  useEffect(() => {
    if (!show || !enabled || !bookId || !periodId || unsupportedScope) { setState({ key }); return; }
    const controller = new AbortController();
    setState({ key });
    service({ bookId, periodId, baselinePeriodId, asOf, accountingBasis: expectedBasis, dimensions }, { signal: controller.signal, allowDevelopmentFallback: false })
      .then((result) => { if (!controller.signal.aborted) setState({ key, result }); })
      .catch((failure: unknown) => { if (!controller.signal.aborted) setState({ key, error: failure instanceof Error ? failure.message : "Comparison could not be loaded." }); });
    return () => controller.abort();
  }, [key, reload, service, show, enabled, bookId, periodId, baselinePeriodId, asOf, expectedBasis, dimensions, unsupportedScope]);
  const current = state.key === key ? state : null;
  const result = current?.result;
  const candidates = periodOptions.filter((option) => option.id !== periodId);
  const unknownBaseline = baselinePeriodId && !candidates.some((option) => option.id === baselinePeriodId);
  function toggle() {
    const next = new URLSearchParams(search);
    if (show) next.delete("comparison"); else next.set("comparison", "prior");
    setSearch(next);
  }
  return (
    <section aria-label="Posted period comparison" className="space-y-3">
      <Button variant="outline" size="sm" aria-pressed={show} onClick={toggle}>Vs prior period</Button>
      {show ? <>
        <FormRow label="Comparison baseline" labelFor={id}>
          <Select id={id} value={baselinePeriodId} onChange={(event) => {
            const next = new URLSearchParams(search);
            if (event.target.value) next.set("baselinePeriodId", event.target.value); else next.delete("baselinePeriodId");
            setSearch(next);
          }}>
            <option value="">Previous completed fiscal period · same elapsed interval</option>
            {unknownBaseline ? <option value={baselinePeriodId}>Selected baseline unavailable in this book</option> : null}
            {candidates.map((option) => <option key={option.id} value={option.id}>{option.label} · {option.statusLabel}</option>)}
          </Select>
        </FormRow>
        {unsupportedScope ? <StatusBanner tone="warning" title="Comparison unavailable" detail={incompleteExternalScope ? "External GL dimension key and value must both be supplied. No unfiltered comparison was requested." : "Share-class scope is not supported by the posted-ledger comparison contract. Clear that scope before comparing; an unfiltered comparison is not a substitute."} />
          : !enabled || !bookId || !periodId ? <p role="status" className="text-sm">Choose an available posted book and period before comparing.</p>
          : current?.error ? <StatusBanner role="alert" tone="warning" title="Comparison unavailable" detail={<>{current.error} <Button variant="outline" size="sm" onClick={() => setReload((value) => value + 1)}>Retry comparison</Button></>} />
          : !result ? <p role="status" className="text-sm">Reading comparable posted journal populations…</p>
          : expectedBasis && String(result.accountingBasis ?? "Unavailable").toLowerCase() !== expectedBasis.toLowerCase() ? <StatusBanner tone="warning" title="Comparison unavailable" detail={`The response does not match the selected ${expectedBasis} basis. No substitute figures are shown.`} />
          : result.status !== "Ready" || !result.baseline ? <StatusBanner tone="warning" title="Comparison unavailable" detail={result.reason ?? "A compatible prior posted period could not be established."} />
          : <>
            <p className="text-sm font-medium">{result.bookName} · {result.accountingBasis} · {result.currency}</p>
            <p className="text-sm">Current: {result.current.label} · {result.current.rangeStart}–{result.current.rangeEnd}{result.current.isPartial ? " · period to date" : " · completed period"}. Prior: {result.baseline.label} · {result.baseline.rangeStart}–{result.baseline.rangeEnd}{result.baseline.isPartial ? " · equivalent elapsed interval" : " · completed period"}.</p>
            <p className="text-xs text-muted-foreground">Account movements from posted entries within these intervals. Opening and cumulative ending balances are not implied. The source cut identifies contributing journals; individual amount support remains in journal proof.</p>
            <TableWorkbench workbenchId="accounting-period-comparison" sourceKey={key}
              sourceDescription={`${result.bookName} · ${result.accountingBasis} · ${result.currency} · current ${result.current.rangeStart}–${result.current.rangeEnd} · prior ${result.baseline.rangeStart}–${result.baseline.rangeEnd} · population ${result.populationHash}`}
              exportContext={{ "Ledger book": result.ledgerBookId, Fund: result.fundProfileId, Basis: result.accountingBasis, Currency: result.currency, "As of": result.asOf, "Current period": result.current.periodId, "Prior period": result.baseline.periodId, "Dimension scope": JSON.stringify(result.dimensions ?? {}) }}
              completePopulation getSearchText={(line) => `${line.accountName} ${line.accountType} ${line.symbol ?? ""}`}
              ariaLabel="Posted period account comparison" rows={result.lines} getRowId={(line) => line.accountKey} emptyText="No posted account activity in these comparable intervals."
              columns={[
                { id: "account", label: "Account", render: (line) => line.accountName, exportValue: (line) => line.accountName },
                { id: "current", label: `Current movement · ${result.currency}`, align: "right", className: "tabular-nums", render: (line) => accountingDecimal(line.currentBalance), exportType: "decimal", exportValue: (line) => line.currentBalance },
                { id: "prior", label: `Prior movement · ${result.currency}`, align: "right", className: "tabular-nums", render: (line) => accountingDecimal(line.priorBalance), exportType: "decimal", exportValue: (line) => line.priorBalance },
                { id: "change", label: `Change · ${result.currency}`, align: "right", className: "tabular-nums", render: (line) => accountingDecimal(line.change), exportType: "decimal", exportValue: (line) => line.change },
                { id: "percent", label: "Change %", align: "right", className: "tabular-nums", render: (line) => line.percentChange === null ? "Not comparable · prior zero" : `${formatTaxDecimal(line.percentChange)}%`, exportType: "decimal", exportValue: (line) => line.percentChange }
              ]} />
            <TechnicalDetails label="Comparison source cut">
              <dl className="space-y-2 text-xs">
                <div><dt>Population fingerprint</dt><dd className="break-all font-mono">{result.populationHash}</dd></div>
                <div><dt>Policy</dt><dd>{result.accountingPolicyId} · {result.accountingPolicyVersion}</dd></div>
                <div><dt>Current journal identities</dt><dd className="break-all font-mono">{result.currentJournalIds.join(", ") || "No posted journals in range"}</dd></div>
                <div><dt>Baseline journal identities</dt><dd className="break-all font-mono">{result.baselineJournalIds.join(", ") || "No posted journals in range"}</dd></div>
              </dl>
            </TechnicalDetails>
          </>}
      </> : null}
    </section>
  );
}
