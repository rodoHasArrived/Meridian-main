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
import type {
  BondAnalyticsQuery,
  BondAnalyticsResult,
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

function decimalText(value: BondDecimal | null | undefined): string {
  return value == null ? "Unavailable" : String(value);
}

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
  const [result, setResult] = useState<BondAnalyticsResult | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    setResult(null);
    setError(null);
    setPending(false);
    setValue("");
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
    if (!asOf || !settlementDate || !value.trim() || !/^-?\d+(?:\.\d+)?$/.test(value.trim())) {
      setError("Enter calculation and settlement dates and a decimal price or annual yield.");
      return;
    }
    const query: BondAnalyticsQuery = {
      asOf: `${asOf}T00:00:00Z`,
      settlementDate,
      yieldConvention: convention,
      ...(knownAt ? { knownAt: `${knownAt}:00Z` } : {}),
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
      setResult(response);
    } catch (failure) {
      if (!controller.signal.aborted)
        setError(describeApiError(failure, "Bond analytics could not be calculated.").summary);
    } finally {
      if (!controller.signal.aborted) setPending(false);
    }
  }

  const metrics: Array<[string, BondDecimal | null | undefined]> = result
    ? [
        ["Clean price / 100", result.cleanPrice],
        ["Dirty price / 100", result.dirtyPrice],
        ["Accrued interest / 100", result.accruedInterest],
        ["YTM (annual fraction)", result.yieldToMaturity],
        ["Current yield (annual fraction)", result.currentYield],
        ["Yield to worst (annual fraction)", result.yieldToWorst],
        ["Macaulay duration (years)", result.macaulayDuration],
        ["Modified duration (years)", result.modifiedDuration],
        ["Convexity", result.convexity],
        ["DV01 / 100", result.dv01]
      ]
    : [];

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
          <fieldset disabled={pending} className="flex min-w-0 flex-col gap-3">
            <legend className="sr-only">Calculation inputs for {securityId}</legend>
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
              <FormRow label="Settlement date" labelFor={`${id}-settlement`}>
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
                  <option value="yield">Annual yield fraction</option>
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
            <Button type="submit" className="w-fit">
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
                    {result.blockers.map((blocker) => (
                      <li key={blocker.code}>
                        {blocker.code}: {blocker.reason}
                      </li>
                    ))}
                  </ul>
                }
              />
            ) : (
              <dl className="grid grid-cols-2 gap-3 xl:grid-cols-5">
                {metrics.map(([label, metric]) => (
                  <div key={label} className="min-w-0">
                    <dt className="text-xs text-muted-foreground">{label}</dt>
                    <dd className="break-words font-mono text-sm tabular-nums">{decimalText(metric)}</dd>
                  </div>
                ))}
              </dl>
            )}
            {result.diagnostics.length ? (
              <StatusBanner
                tone="warning"
                title="Calculation diagnostics"
                detail={
                  <ul className="list-disc pl-4">
                    {result.diagnostics.map((diagnostic, index) => (
                      <li key={index}>{diagnostic}</li>
                    ))}
                  </ul>
                }
              />
            ) : null}
            <details>
              <summary className="cursor-pointer text-sm font-semibold">
                Projected contractual payments ({result.cashFlows.length})
              </summary>
              <p className="my-2 text-xs text-muted-foreground">
                Projected cash per 100 face; these rows are not observed payments or posted journals.
              </p>
              <DenseDataTable
                columns={cashFlowColumns}
                rows={result.cashFlows}
                getRowId={(row) => row.flowId}
                ariaLabel="Projected bond payments"
                caption="Projected contractual payments per 100 face"
                emptyText="No calculated payment schedule is available."
              />
            </details>
            {result.exerciseYields.length ? (
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
                    {input.version ?? input.versionId ?? "Unspecified"}
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
