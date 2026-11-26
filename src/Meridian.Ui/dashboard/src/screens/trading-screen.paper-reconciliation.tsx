import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { apiGetJson } from "@/lib/api";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { formatTaxAmount, formatTaxDecimal } from "@/lib/ledger-tax-results-format";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { routeWithParam } from "@/lib/workstation-endpoints";

import { VALUE_STATE_LABELS } from "@/lib/format";
interface PaperCutoffEvidence {
  sessionId: string;
  accountId: string;
  currency: string | null;
  cutoff: string;
  journalCash: string | null;
  portfolioCash: string | null;
  breaks: { accountId: string; check: string; expected: string; actual: string; difference: string }[];
  coverageBlocker: string | null;
  sourceHashSha256?: string;
}

export function PaperReconciliationPanel({ sessionId }: { sessionId: string }) {
  const controller = useRef<AbortController | null>(null);
  const [status, setStatus] = useState<"idle" | "loading" | "empty" | "available">("idle");
  const [evidence, setEvidence] = useState<PaperCutoffEvidence | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => () => controller.current?.abort(), []);

  const refresh = async () => {
    if (controller.current) return;
    const request = new AbortController();
    controller.current = request;
    setStatus("loading");
    setError(null);
    setEvidence(null);
    try {
      const result = await apiGetJson<PaperCutoffEvidence>(
        routeWithParam(UI_API_ROUTES.ExecutionSessionReconciliation, "sessionId", sessionId),
        { signal: request.signal, allowDevelopmentFallback: false }
      );
      if (result?.sessionId !== sessionId) throw new Error("The reconciliation evidence belongs to another paper session.");
      if (!request.signal.aborted) {
        setEvidence(result);
        setStatus("available");
      }
    } catch (failure) {
      if (request.signal.aborted) return;
      if (isApiError(failure) && failure.status === 404) setStatus("empty");
      else {
        setStatus("idle");
        setError(describeApiError(failure, "The scheduled reconciliation could not be read.").summary);
      }
    } finally {
      if (controller.current === request) controller.current = null;
    }
  };

  return <section className="space-y-2 border-t border-border pt-3" aria-label="Paper daily reconciliation">
    <Button variant="outline" size="sm" disabled={status === "loading"} onClick={() => void refresh()}>
      {status === "loading" ? "Loading daily reconciliation…" : "Review daily reconciliation"}
    </Button>
    {status === "empty" ? <p role="status" className="text-sm text-muted-foreground">This session has no retained daily reconciliation.</p> : null}
    {error ? <StatusBanner tone="danger" role="alert" title="Reconciliation unavailable" detail={error} /> : null}
    {evidence ? <>
      <StatusBanner tone={evidence.coverageBlocker || evidence.breaks.length ? "warning" : "success"}
        title={evidence.coverageBlocker ? "Coverage requires review" : evidence.breaks.length ? "Ledger breaks require review" : "Session cash and lots tie"}
        detail={evidence.coverageBlocker ?? evidence.cutoff} />
      <dl className="grid gap-2 text-sm sm:grid-cols-2">
        {([["Journal cash", evidence.journalCash], ["Portfolio cash", evidence.portfolioCash]] as const).map(([label, amount]) =>
          <div key={label}><dt className="text-muted-foreground">{label}</dt>
            <dd className="font-mono">{!evidence.currency ? VALUE_STATE_LABELS.unavailable : formatTaxAmount(amount, evidence.currency)}</dd>
          </div>)}
      </dl>
      {evidence.breaks.length ? <ul className="space-y-1 text-sm">{evidence.breaks.map((item, index) =>
        <li key={`${item.check}-${index}`}>{item.check} · actual {formatTaxDecimal(item.actual)} · expected {formatTaxDecimal(item.expected)} · difference {formatTaxDecimal(item.difference)}</li>)}</ul> : null}
      {evidence.sourceHashSha256 ? <p className="break-all text-xs text-muted-foreground">Source hash <code>{evidence.sourceHashSha256}</code></p> : null}
    </> : null}
  </section>;
}
