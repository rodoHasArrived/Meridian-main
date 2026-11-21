import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { getSecurityInstrumentPassport } from "@/lib/api";
import type { InstrumentPassport } from "@/types";

export interface AssetPassportSummaryProps {
  securityId: string;
  onOpenPassport: () => void;
}

type PassportState =
  | { securityId: string; status: "loading" }
  | { securityId: string; status: "error" }
  | { securityId: string; status: "ready"; passport: InstrumentPassport };

function textField(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value : null;
}

function trustVariant(tone: string) {
  switch (tone.toLowerCase()) {
    case "trusted":
      return "success";
    case "blocked":
      return "danger";
    case "review":
      return "warning";
    default:
      return "outline";
  }
}

/** Read-only server context. Instrument status never substitutes for this readiness assessment. */
export function AssetPassportSummary({ securityId, onOpenPassport }: AssetPassportSummaryProps) {
  const [state, setState] = useState<PassportState>({
    securityId,
    status: "loading"
  });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    setState({ securityId, status: "loading" });
    getSecurityInstrumentPassport(securityId, {
      signal: controller.signal,
      allowDevelopmentFallback: false
    })
      .then((passport) => {
        if (passport.securityId !== securityId || passport.identity.securityId !== securityId) {
          throw new Error("Passport identity does not match the selected security.");
        }
        if (!cancelled) setState({ securityId, status: "ready", passport });
      })
      .catch(() => {
        if (!cancelled) setState({ securityId, status: "error" });
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [securityId, attempt]);

  if (state.securityId !== securityId || state.status === "loading") {
    return (
      <section aria-label="Asset readiness" className="border border-border bg-background/60 px-4 py-3">
        <p role="status" className="text-sm text-muted-foreground">
          Loading readiness and instrument context…
        </p>
      </section>
    );
  }
  if (state.status === "error") {
    return (
      <section
        aria-label="Asset readiness"
        className="flex flex-wrap items-center justify-between gap-3 border border-border bg-background/60 px-4 py-3"
      >
        <p role="alert" className="text-sm">
          Couldn’t load readiness and instrument context.
        </p>
        <Button size="sm" variant="outline" onClick={() => setAttempt((value) => value + 1)}>
          Retry readiness
        </Button>
      </section>
    );
  }

  const { passport } = state;
  const conflicts = new Map<string, string>();
  for (const provider of passport.providerConfidence ?? []) {
    for (const [index, conflictId] of provider.identifierConflictIds.entries()) {
      conflicts.set(
        conflictId,
        provider.identifierConflictSummaries[index] ?? `${provider.provider}: identifier conflict ${conflictId}`
      );
    }
  }
  const readiness = passport.operationsWorkbench?.readiness;
  const reviewItems = readiness?.filter((item) => !item.isReady) ?? [];
  const approval = passport.operatingModel?.manualChangeApproval;
  const definition = passport.economicDefinition;
  const source = textField(definition.winningSourceSystem);
  const sourceAsOf = textField(definition.winningSourceAsOf);
  const fields = [
    {
      label: "Settlement model",
      value: passport.classificationProfile?.settlementModel
    },
    { label: "Risk country", value: textField(definition.riskCountry) },
    { label: "Issuer type", value: textField(definition.issuerType) },
    {
      label: "Contract multiplier",
      value: passport.pricing.contractMultiplier == null ? null : String(passport.pricing.contractMultiplier)
    }
  ].filter(
    (field): field is { label: string; value: string } =>
      typeof field.value === "string" && field.value.trim().length > 0
  );

  return (
    <section
      aria-labelledby="asset-readiness-title"
      className="space-y-3 border border-border bg-background/60 px-4 py-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="asset-readiness-title" className="text-sm font-semibold">
          Readiness & evidence
        </h3>
        <Button size="sm" variant="outline" onClick={onOpenPassport}>
          Open governed passport
        </Button>
      </div>
      <dl className="grid grid-cols-2 gap-3">
        <div>
          <dt className="text-xs text-muted-foreground">Data trust</dt>
          <dd className="mt-1">
            <Badge variant={trustVariant(passport.trustPosture.tone)}>{passport.trustPosture.tone || "Unknown"}</Badge>
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Provider identifier conflicts</dt>
          <dd className="mt-1 text-sm font-semibold tabular-nums">
            {passport.providerConfidence?.length ? conflicts.size : "No provider evidence"}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Operations requiring review</dt>
          <dd className="mt-1 text-sm font-semibold tabular-nums">
            {readiness?.length ? reviewItems.length : "Not assessed"}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Unapproved manual changes</dt>
          <dd className="mt-1 text-sm font-semibold tabular-nums">
            {approval?.unapprovedManualChangeCount ?? "Not assessed"}
          </dd>
        </div>
      </dl>
      <p className="text-sm text-muted-foreground">{passport.trustPosture.summary}</p>
      {conflicts.size || reviewItems.length || (approval?.unapprovedManualChangeCount ?? 0) > 0 ? (
        <details className="border-t border-border pt-2">
          <summary className="cursor-pointer text-sm font-medium focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40">
            Inspect readiness evidence
          </summary>
          <ul className="mt-2 space-y-2 text-sm">
            {[...conflicts].map(([id, summary]) => (
              <li key={id}>
                <span className="font-semibold">Identifier conflict: </span>
                {summary}
              </li>
            ))}
            {reviewItems.map((item) => (
              <li key={item.readinessId}>
                <span className="font-semibold">
                  {item.label} · {item.status}:{" "}
                </span>
                {item.summary}
                {item.nextAction ? <span className="block text-muted-foreground">{item.nextAction}</span> : null}
              </li>
            ))}
            {(approval?.unapprovedManualChangeCount ?? 0) > 0 ? (
              <li>
                <span className="font-semibold">Manual change approval: </span>
                {approval?.summary}
              </li>
            ) : null}
          </ul>
        </details>
      ) : null}
      {fields.length ? (
        <dl aria-label="Instrument context" className="flex flex-wrap gap-x-6 gap-y-2 border-t border-border pt-2">
          {fields.map((field) => (
            <div key={field.label}>
              <dt className="text-xs text-muted-foreground">{field.label}</dt>
              <dd className="mt-1 text-sm tabular-nums">{field.value}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      <p className="text-xs text-muted-foreground">
        Passport retrieved {passport.retrievedAtUtc || "at an unknown time"}
        {source ? ` · Economic source ${source}` : ""}
        {sourceAsOf ? ` · Source as of ${sourceAsOf}` : ""}
      </p>
    </section>
  );
}
