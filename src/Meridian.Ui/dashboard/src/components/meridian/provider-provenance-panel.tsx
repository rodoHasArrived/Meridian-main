import { useState } from "react";
import { Button } from "@/components/ui/button";
import { getBackfillClonePlan, runBackfillClone } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import type {
  BackfillClonePlan,
  ProviderProvenanceReceipt,
  ProviderProvenanceRequest,
  ProviderProvenanceSource,
  ProviderProvenanceValidation
} from "@/types/provider-provenance";

const validationFields = {
  version: "Profile version",
  staleToleranceDays: "Stale tolerance (days)",
  futureToleranceDays: "Future tolerance (days)",
  tryOtherProvidersForStaleBars: "Try other providers for stale bars",
  symbolResolution: "Symbol resolution",
  crossValidation: "Cross-validation",
  rateLimitRotation: "Rate-limit rotation",
  rateLimitRotationThreshold: "Rate-limit rotation threshold (fraction)",
  rateLimitRetries: "Rate-limit retries",
  rateLimitRetryBudgetSeconds: "Rate-limit retry budget (seconds)",
  failureBackoffSeconds: "Failure backoff (seconds)",
  preflightEnabled: "Preflight validation",
  corporateActionsApplied: "Corporate actions applied",
  failOnUnknownSymbols: "Fail on unknown symbols",
  optionsStrikeRange: "Options strike range"
} satisfies Record<keyof ProviderProvenanceValidation, string>;

const exportKeys = [
  "schemaVersion", "receiptId", "capability", "routingDecision", "providers", "fallbackChain", "fallbackOccurred",
  "validationProfile", "request", "startedUtc", "completedUtc", "dataSemantics", "reproductionUnavailableReason", "routingHistoryComplete",
  "providerId", "connectionId", "environment", "feedLabel", "entitlementLabel", "priority", "requests",
  "responsesWithData", "unavailable", "emptyResponses", "staleResponses", "staleResponsesAccepted", "replayEvents",
  "from", "to", "symbols", "granularity", "maxConcurrentSymbols", "symbolPriorities", "symbol", "resumeFromCheckpoint",
  "initialCash", "adjustForCorporateActions", "failOnUnknownSymbols", "withheldSymbolCount", "usesDiscoveredUniverse", "skipExistingData", "fillGapsOnly",
  ...Object.keys(validationFields)
];

type Field = readonly [label: string, value: string | number | boolean | null | undefined];

function fieldText(value: Field[1]): string {
  return typeof value === "boolean" ? (value ? "Yes" : "No") : String(value ?? "Not recorded");
}

function requestFields(request: ProviderProvenanceRequest): Field[] {
  const fields: Field[] = [
    ["Requested from", request.from ?? "Provider default"],
    ["Requested to", request.to ?? "Provider default"],
    ["Requested symbols", request.usesDiscoveredUniverse ? "Discovered universe" : request.symbols.join(", ") || "Not recorded"],
    ["Symbols withheld", request.withheldSymbolCount || null],
    ["Granularity", request.granularity],
    ["Concurrent symbols", request.maxConcurrentSymbols],
    ["Symbol priorities", request.symbolPriorities?.map(({ symbol, priority }) => `${symbol} (${priority})`).join(", ") || null],
    ["Resume from checkpoint", request.resumeFromCheckpoint],
    ["Skip existing data", request.skipExistingData],
    ["Fill gaps only", request.fillGapsOnly],
    ["Initial cash", request.initialCash],
    ["Adjust for corporate actions", request.adjustForCorporateActions],
    ["Fail on unknown symbols", request.failOnUnknownSymbols]
  ];
  return fields.filter((field) => field[1] != null);
}

function receiptFields(receipt: ProviderProvenanceReceipt): Field[] {
  return [
    ["Schema version", receipt.schemaVersion], ["Receipt ID", receipt.receiptId], ["Capability", receipt.capability],
    ["Data semantics", receipt.dataSemantics], ["Routing decision", receipt.routingDecision],
    ["Fallback chain", receipt.fallbackChain.join(" → ") || "None"],
    ["Fallback occurred", receipt.routingHistoryComplete === false && !receipt.fallbackOccurred ? "Earlier decisions unavailable" : receipt.fallbackOccurred],
    ["Started (UTC)", receipt.startedUtc], ["Completed (UTC)", receipt.completedUtc]
  ];
}

function providerFields(source: ProviderProvenanceSource): Field[] {
  const fields: Field[] = [
    ["Connection ID", source.connectionId], ["Environment", source.environment], ["Feed", source.feedLabel],
    ["Entitlement", source.entitlementLabel], ["Data semantics", source.dataSemantics]
  ];
  if (source.staleToleranceDays != null) {
    fields.push(["Stale tolerance (days)", source.staleToleranceDays]);
  }
  if (source.replayEvents > 0) {
    fields.push(["Replayed events", source.replayEvents]);
  } else if (source.priority != null) {
    fields.push(["Priority", source.priority], ["Requests", source.requests], ["Responses with data", source.responsesWithData],
      ["Unavailable or failed", source.unavailable], ["Empty responses", source.emptyResponses], ["Stale responses", source.staleResponses],
      ["Stale responses accepted", source.staleResponsesAccepted]);
  }
  return fields;
}

function profileFields(profile: ProviderProvenanceValidation): Field[] {
  return (Object.keys(validationFields) as (keyof ProviderProvenanceValidation)[])
    .filter((key) => profile[key] != null)
    .map((key) => [validationFields[key], profile[key]]);
}

export function serializeProviderProvenance(receipt: ProviderProvenanceReceipt, format: "json" | "txt"): string {
  if (format === "json") return `${JSON.stringify(receipt, exportKeys, 2)}\n`;

  const lines = (fields: Field[]) => fields.map(([label, value]) => `${label}  ${fieldText(value)}`);
  return [
    "Provider provenance receipt", "", ...lines(receiptFields(receipt)), "",
    ...receipt.providers.flatMap((source) => [`Provider ${source.providerId}`, ...lines(providerFields(source)), ""]),
    "Request", ...lines(requestFields(receipt.request)), "", "Validation profile", ...lines(profileFields(receipt.validationProfile)),
    ...(receipt.reproductionUnavailableReason ? ["", "Reproduction unavailable", receipt.reproductionUnavailableReason] : []), ""
  ].join("\n");
}

function downloadReceipt(receipt: ProviderProvenanceReceipt, format: "json" | "txt") {
  const type = format === "json" ? "application/json" : "text/plain;charset=utf-8";
  const url = URL.createObjectURL(new Blob([serializeProviderProvenance(receipt, format)], { type }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `provider-provenance-${receipt.receiptId}-v${receipt.schemaVersion}.${format}`;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

function FieldList({ fields }: { fields: Field[] }) {
  return (
    <dl className="grid min-w-0 gap-x-4 gap-y-1 text-xs sm:grid-cols-[minmax(7rem,1fr)_minmax(0,2fr)]">
      {fields.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="break-words text-foreground">{fieldText(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

function ReceiptDetails({ label, fields }: { label: string; fields: Field[] }) {
  return (
    <details className="border-t border-border pt-2">
      <summary className="cursor-pointer break-words rounded-sm text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
        {label}
      </summary>
      <div className="mt-2"><FieldList fields={fields} /></div>
    </details>
  );
}

function CloneRequest({ receipt, onBackfillCompleted }: {
  receipt: ProviderProvenanceReceipt;
  onBackfillCompleted?: () => void;
}) {
  const [plan, setPlan] = useState<BackfillClonePlan | null>(null);
  const [busy, setBusy] = useState(false);
  const [completed, setCompleted] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function review() {
    setMessage(null);
    setCompleted(false);
    setPlan(null);
    if (receipt.reproductionUnavailableReason || receipt.capability !== "HistoricalBars") {
      setPlan({ request: receipt.request, status: "Unavailable", messages: [
        receipt.reproductionUnavailableReason ?? "The historical configuration was unavailable."
      ] });
      return;
    }

    setBusy(true);
    try {
      setPlan(await getBackfillClonePlan(receipt.receiptId));
    } catch (error) {
      setMessage(isApiError(error) && error.status === 403
        ? "Backfill permission is required to reproduce this run."
        : "The historical configuration could not be checked. Try again.");
    } finally {
      setBusy(false);
    }
  }

  async function confirm() {
    if (busy || !plan?.confirmationCode) return;
    setBusy(true);
    setMessage(null);
    try {
      const result = await runBackfillClone(receipt.receiptId, plan.confirmationCode);
      setMessage(result.success ? "Clone completed. Its receipt is in the backfill queue." : "The cloned run ended in review. Inspect its receipt in the backfill queue.");
      setCompleted(true);
      onBackfillCompleted?.();
    } catch (error) {
      if (isApiError(error) && error.status === 409) {
        await review();
        setMessage("Review the latest availability and policy before confirming again.");
      } else {
        setMessage("The clone could not be run. Review the request and try again.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-2">
      <Button type="button" variant="outline" size="sm" onClick={() => void review()} disabled={busy}>
        {plan ? "Review clone again" : busy ? "Checking configuration…" : "Clone this run"}
      </Button>
      {plan ? (
        <div className="space-y-2 rounded-md border border-border p-3">
          <p className="text-sm font-semibold" role="status">
            {plan.status === "Unavailable" ? "Reproduction unavailable" : plan.status === "ReconfirmationRequired" ? "Reconfirm provider availability" : "Review restored request"}
          </p>
          <FieldList fields={requestFields(plan.request)} />
          <ul className="list-disc space-y-1 pl-4 text-xs text-muted-foreground">
            {plan.messages.map((item) => <li key={item}>{item}</li>)}
          </ul>
          {plan.confirmationCode ? (
            <Button type="button" size="sm" onClick={() => void confirm()} disabled={busy || completed}>
              {busy ? "Running clone…" : "Confirm and run clone"}
            </Button>
          ) : null}
        </div>
      ) : null}
      {message ? <p className="text-xs text-muted-foreground" role="status">{message}</p> : null}
    </div>
  );
}

export function ProviderProvenancePanel({ receipt, onBackfillCompleted }: {
  receipt?: ProviderProvenanceReceipt | null;
  onBackfillCompleted?: () => void;
}) {
  if (!receipt) return <p className="mt-3 text-xs text-muted-foreground">Provider provenance was unavailable for this result.</p>;

  const actual = receipt.providers.filter((source) => source.replayEvents > 0 || source.responsesWithData > 0 || source.staleResponsesAccepted > 0);
  const summary = (actual.length > 0 ? actual : receipt.providers).map((source) => source.providerId).join(", ");
  return (
    <details className="mt-3 min-w-0 rounded-md border border-border bg-background/30">
      <summary className="cursor-pointer break-words rounded-md px-3 py-2 text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-primary">
        Provider provenance <span className="font-normal text-muted-foreground">· {summary || "Unavailable"}{receipt.fallbackOccurred ? " · Fallback used" : ""}</span>
      </summary>
      <div className="space-y-3 border-t border-border p-3">
        <FieldList fields={receiptFields(receipt)} />
        {receipt.providers.map((source, index) => (
          <ReceiptDetails key={`${source.providerId}-${index}`} label={`Provider ${source.providerId}`} fields={providerFields(source)} />
        ))}
        <ReceiptDetails label="Saved request" fields={requestFields(receipt.request)} />
        <ReceiptDetails label={`Validation profile · ${receipt.validationProfile.version}`} fields={profileFields(receipt.validationProfile)} />
        {receipt.reproductionUnavailableReason ? (
          <p className="text-xs text-muted-foreground">Reproduction unavailable. {receipt.reproductionUnavailableReason}</p>
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => downloadReceipt(receipt, "json")}>Export JSON sidecar</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => downloadReceipt(receipt, "txt")}>Export report</Button>
        </div>
        <CloneRequest key={receipt.receiptId} receipt={receipt} onBackfillCompleted={onBackfillCompleted} />
      </div>
    </details>
  );
}
