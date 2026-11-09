import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { getBondAnalytics } from "@/lib/api/bond-analytics.api";
import { describeApiError } from "@/lib/api-errors";
import { DECIMAL_LITERAL, bondDecimalText, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import type {
  BondAnalyticsQuery,
  BondAnalyticsResult,
  BondCalculationVersion,
  BondDecimal,
  BondYieldConvention
} from "@/types/bond-analytics.types";

type CashFlow = BondAnalyticsResult["cashFlows"][number];
const cashFlowColumns: DenseDataTableColumn<CashFlow>[] = [
  { id: "payment", label: "Payment date", render: (row) => row.paymentDate },
  {
    id: "accrual",
    label: "Accrual period",
    render: (row) => `${row.accrualStart} – ${row.accrualEnd}`
  },
  {
    id: "interest",
    label: "Interest / 100",
    align: "right",
    render: (row) => decimalText(row.interest)
  },
  {
    id: "principal",
    label: "Principal / 100",
    align: "right",
    render: (row) => decimalText(row.principal)
  }
];

const decimalText = (value: BondDecimal | null | undefined): string => bondDecimalText(value);

const floatingCashFlowColumns: DenseDataTableColumn<CashFlow>[] = [
  ...cashFlowColumns,
  {
    id: "fixing",
    label: "Fixing / source",
    render: (row) => row.floatingCoupon ? (
      <div className="max-w-60 whitespace-normal text-xs">
        <strong className="break-words">
          {row.floatingCoupon.isObserved ? "Observed fixing" : "Forecast fixing"} · {row.floatingCoupon.indexName}
        </strong>
        <span className="block">Reset {row.floatingCoupon.resetDate}</span>
        <span className="block break-words">{row.floatingCoupon.source || "Source unavailable"}</span>
      </div>
    ) : "Fixing evidence unavailable"
  },
  {
    id: "rates",
    label: "Coupon rate basis",
    align: "right",
    render: (row) => row.floatingCoupon ? (
      <div className="whitespace-nowrap text-xs">
        <span className="block">Index fraction: {decimalText(row.floatingCoupon.indexRate)}</span>
        <span className="block">Spread: {decimalText(row.floatingCoupon.spreadBps)} bp</span>
        <span className="block">Coupon fraction: {decimalText(row.floatingCoupon.couponRate)}</span>
      </div>
    ) : "Unavailable"
  }
];

export interface BondAnalyticsPanelProps {
  securityId: string;
  calculate?: typeof getBondAnalytics;
}

/** User-triggered analysis; no client-side financial calculation, mutation, or imported-metric overwrite. */
export function BondAnalyticsPanel({ securityId, calculate = getBondAnalytics }: BondAnalyticsPanelProps) {
  const id = useId();
  const [asOf, setAsOf] = useState(() => new Date().toISOString().slice(0, 10));
  const [settlementDate, setSettlementDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [knownAt, setKnownAt] = useState("");
  const [inputKind, setInputKind] = useState<"cleanPrice" | "yield">("cleanPrice");
  const [value, setValue] = useState("");
  const [convention, setConvention] = useState<BondYieldConvention>("NominalPeriodic");
  const [calculationVersion, setCalculationVersion] = useState<BondCalculationVersion>("bond-analytics-v2");
  const [result, setResult] = useState<BondAnalyticsResult | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    setResult(null);
    setError(null);
    setPending(false);
    setValue("");
    setCalculationVersion("bond-analytics-v2");
    return () => request.current?.abort();
  }, [securityId]);

  function edit(change: () => void) {
    request.current?.abort();
    setPending(false);
    setResult(null);
    setError(null);
    change();
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setResult(null);
    setError(null);
    if (!asOf || !settlementDate || !value.trim() || !DECIMAL_LITERAL.test(value.trim())) {
      setError("Enter calculation and settlement dates and a decimal price or annual yield.");
      return;
    }
    const query: BondAnalyticsQuery = {
      asOf: `${asOf}T00:00:00Z`,
      settlementDate,
      calculationVersion,
      yieldConvention: convention,
      ...(knownAt ? { knownAt: utcInstantFromLocalInput(knownAt) } : {}),
      [inputKind]: value.trim()
    };
    setPending(true);
    try {
      const response = await calculate(securityId, query, {
        signal: controller.signal,
        allowDevelopmentFallback: false
      });
      if (controller.signal.aborted) return;
      if (response.securityId.toLowerCase() !== securityId.toLowerCase())
        throw new Error("The calculation returned a different security. Refresh the selected record.");
      if (response.modelVersion !== calculationVersion)
        throw new Error("The calculation returned a different model version. No result is displayed; refresh and retry the selected version.");
      setResult(response);
    } catch (failure) {
      if (!controller.signal.aborted)
        setError(describeApiError(failure, "Bond analytics could not be calculated.").summary);
    } finally {
      if (!controller.signal.aborted) setPending(false);
    }
  }

  const isFloating = calculationVersion === "bond-analytics-v3";
  const metrics: Array<[string, BondDecimal | null | undefined]> = result
    ? [
        ["Clean price / 100", result.cleanPrice],
        ["Dirty price / 100", result.dirtyPrice],
        ["Accrued interest / 100", result.accruedInterest],
        [isFloating ? "Projected-flow yield (annual fraction)" : "YTM (annual fraction)", result.yieldToMaturity],
        ["Current yield (annual fraction)", result.currentYield],
        ["Macaulay duration (years)", result.macaulayDuration],
        ["Modified duration (years)", result.modifiedDuration],
        ["Convexity (years²)", result.convexity],
        ["DV01 (price points / 100 / bp)", result.dv01]
      ]
    : [];
  if (result) {
    metrics.push(isFloating
      ? ["SpreadDv01 (price points / 100 / +1 bp)", result.spreadDv01]
      : ["Yield to worst (annual fraction)", result.yieldToWorst]);
  }

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <CardTitle>Bond analytics</CardTitle>
          <Badge variant="outline">Calculated analysis</Badge>
        </div>
        <CardDescription>
          Settlement-aware values from retained bond terms. Imported Pricing &amp; Risk fields above remain separate.
          This analysis grants no posting or trading authority.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <form onSubmit={submit} aria-label="Bond analytics inputs">
          <fieldset className="flex min-w-0 flex-col gap-3">
            <legend className="sr-only">Calculation inputs for {securityId}</legend>
            <FormRow
              label="Calculation version"
              labelFor={`${id}-model`}
              hint="The selected model must match the retained bond terms. Changing a model or input clears the calculation."
            >
              <Select
                id={`${id}-model`}
                value={calculationVersion}
                onChange={(e) => edit(() => setCalculationVersion(e.target.value as BondCalculationVersion))}
              >
                <option value="bond-analytics-v1">bond-analytics-v1 · Legacy fixed / zero coupon</option>
                <option value="bond-analytics-v2">bond-analytics-v2 · Fixed / zero coupon</option>
                <option value="bond-analytics-v3">bond-analytics-v3 · Floating, named index</option>
              </Select>
            </FormRow>
            {isFloating ? (
              <p className="text-sm text-muted-foreground">
                v3 requires an explicitly typed Floating bond with a named reference-rate index. The yield discounts
                projected coupon cash flows and is not a discount margin. Already-reset periods require observed
                fixings; future resets require a retained forward curve. Missing inputs block the calculation.
              </p>
            ) : null}
            <FormGrid columns={3}>
              <FormRow label="Calculation date (UTC)" labelFor={`${id}-asof`}>
                <Input
                  id={`${id}-asof`}
                  type="date"
                  required
                  value={asOf}
                  onChange={(e) => edit(() => setAsOf(e.target.value))}
                />
              </FormRow>
              <FormRow
                label="Settlement date"
                labelFor={`${id}-settlement`}
                hint="Defaults to the calculation date; enter the contractual T+N settlement for a traded price."
              >
                <Input
                  id={`${id}-settlement`}
                  type="date"
                  required
                  value={settlementDate}
                  onChange={(e) => edit(() => setSettlementDate(e.target.value))}
                />
              </FormRow>
              <FormRow label="Recorded cutoff (UTC, optional)" labelFor={`${id}-known`}>
                <Input
                  id={`${id}-known`}
                  type="datetime-local"
                  value={knownAt}
                  onChange={(e) => edit(() => setKnownAt(e.target.value))}
                />
              </FormRow>
              <FormRow label="Input basis" labelFor={`${id}-basis`}>
                <Select
                  id={`${id}-basis`}
                  value={inputKind}
                  onChange={(e) =>
                    edit(() => {
                      setInputKind(e.target.value as "cleanPrice" | "yield");
                      setValue("");
                    })
                  }
                >
                  <option value="cleanPrice">Clean price per 100 face</option>
                  <option value="yield">{isFloating ? "Projected-flow annual yield fraction" : "Annual yield fraction"}</option>
                </Select>
              </FormRow>
              <FormRow
                label={inputKind === "cleanPrice" ? "Clean price / 100" : "Annual yield (0.05 = 5%)"}
                labelFor={`${id}-value`}
              >
                <Input
                  id={`${id}-value`}
                  inputMode="decimal"
                  required
                  value={value}
                  onChange={(e) => edit(() => setValue(e.target.value))}
                />
              </FormRow>
              <FormRow label="Yield convention" labelFor={`${id}-convention`}>
                <Select
                  id={`${id}-convention`}
                  value={convention}
                  onChange={(e) => edit(() => setConvention(e.target.value as BondYieldConvention))}
                >
                  <option value="NominalPeriodic">Nominal, coupon frequency</option>
                  <option value="AnnualEffective">Annual effective (zero: ACT/365F)</option>
                  <option value="Simple">Simple (zero: ACT/365F)</option>
                </Select>
              </FormRow>
            </FormGrid>
            <Button type="submit" className="w-fit" disabled={pending} aria-busy={pending}>
              {pending ? "Calculating…" : "Calculate bond analytics"}
            </Button>
          </fieldset>
        </form>
        <span role="status" aria-live="polite">
          {pending
            ? "Calculating from retained inputs…"
            : result
              ? result.isBlocked
                ? "Calculation blocked. Review the reasons below."
                : "Calculation ready."
              : "Enter inputs to calculate. No market price is assumed."}
        </span>
        {error ? <StatusBanner tone="danger" title="Calculation unavailable" detail={error} role="alert" /> : null}
        {result ? (
          <>
            <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
              <span>Model: {result.modelVersion}</span>
              <span>Currency: {result.currency}</span>
              <span>Settlement: {result.context.settlementDate ?? "Unavailable"}</span>
              <span>Yield: {result.yieldConvention ?? "Unspecified"}</span>
            </div>
            {result.isBlocked ? (
              <StatusBanner
                tone="warning"
                title="Calculation blocked"
                detail={
                  <ul className="list-disc pl-4">
                    {result.blockers.map((blocker, index) => (
                      <li key={`${blocker.code}:${index}`}>
                        {blocker.code}: {blocker.reason}
                      </li>
                    ))}
                  </ul>
                }
              />
            ) : (
              <>
                <dl className="grid grid-cols-2 gap-3 xl:grid-cols-5">
                  {metrics.map(([label, metric]) => (
                    <div key={label} className="min-w-0">
                      <dt className="text-xs text-muted-foreground">{label}</dt>
                      <dd className="break-words font-mono text-sm tabular-nums">{decimalText(metric)}</dd>
                    </div>
                  ))}
                </dl>
                <p className="text-xs text-muted-foreground">
                  DV01 is the approximate price decrease per 100 face for a 1 bp yield increase, holding cash flows fixed.
                  {isFloating ? " SpreadDv01 is the signed dirty-price change per 100 face for a +1 bp coupon-spread increase at the same projected-flow yield, including any cap or floor. It is separate from DV01." : null}
                </p>
              </>
            )}
            {result.diagnostics.length ? (
              <StatusBanner
                tone="warning"
                title="Calculation diagnostics"
                detail={
                  <ul className="list-disc pl-4">
                    {result.diagnostics.map((diagnostic, index) => (
                      <li key={`${index}:${diagnostic}`}>{diagnostic}</li>
                    ))}
                  </ul>
                }
              />
            ) : null}
            {!result.isBlocked ? (
              <details>
                <summary className="cursor-pointer text-sm font-semibold">
                  Projected contractual payments ({result.cashFlows.length})
                </summary>
                <p className="my-2 text-xs text-muted-foreground">
                  Projected cash per 100 face; these rows are not observed payments or posted journals.
                  {isFloating ? " Observed or forecast describes the fixing, not the payment. Reset status follows the calculation date; rates are annual fractions (0.03 = 3%) and spread is in basis points (100 bp = 1%)." : null}
                </p>
                <DenseDataTable
                  columns={isFloating ? floatingCashFlowColumns : cashFlowColumns}
                  rows={result.cashFlows}
                  getRowId={(row) => row.flowId}
                  ariaLabel="Projected bond payments"
                  caption="Projected contractual payments per 100 face"
                  emptyText="No calculated payment schedule is available."
                />
              </details>
            ) : null}
            {!result.isBlocked && result.exerciseYields.length ? (
              <details>
                <summary className="cursor-pointer text-sm font-semibold">Contractual exercise scenarios</summary>
                <ul className="mt-2 flex flex-col gap-2 text-xs">
                  {result.exerciseYields.map((exercise) => (
                    <li key={`${exercise.kind}-${exercise.exerciseDate}`}>
                      {exercise.kind} · {exercise.exerciseDate} · redemption {decimalText(exercise.redemptionPrice)} /
                      100 · annual yield {decimalText(exercise.yield)} · latest notice{" "}
                      {exercise.latestNoticeDate ?? "Not specified"}
                    </li>
                  ))}
                </ul>
              </details>
            ) : null}
            <details>
              <summary className="cursor-pointer text-sm font-semibold">Input provenance and operation support</summary>
              <p className="my-2 text-xs text-muted-foreground">
                Recorded cutoffs select eligible retained rows; they do not prove historical transaction visibility.
              </p>
              <ul className="flex flex-col gap-2 text-xs">
                {result.inputs.map((input, index) => (
                  <li className="break-all" key={`${input.kind}-${input.key}-${index}`}>
                    <strong>{input.kind}</strong> · {input.key} · version{" "}
                    {input.version ?? "Unspecified"}
                    {input.versionId ? <span className="block">Version ID {input.versionId}</span> : null}
                    {input.contentSha256 ? (
                      <span className="block font-mono">SHA-256 {input.contentSha256}</span>
                    ) : null}
                    {input.asOf ? <span className="block">As of {input.asOf}</span> : null}
                    {input.recordedAt ? <span className="block">Recorded {input.recordedAt}</span> : null}
                  </li>
                ))}
              </ul>
              <ul className="mt-3 flex flex-col gap-2 text-xs">
                {result.capabilities.map((capability) => (
                  <li key={capability.operation}>
                    <Badge variant={capability.supported ? "success" : "outline"}>
                      {capability.operation}: {capability.supported ? "Supported" : "Unavailable"}
                    </Badge>
                    {capability.reason ? <span className="ml-2">{capability.reason}</span> : null}
                  </li>
                ))}
              </ul>
            </details>
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}
