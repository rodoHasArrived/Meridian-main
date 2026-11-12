import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { getAssetOperations } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { workstationRouteWithQuery } from "@/lib/workspace";
import type { CashLadderDrillRow } from "@/screens/cash-ladder-screen.view-model";
import type { AssetCashFlowProjectionRun, AssetTermsVersion } from "@/types";

interface ProjectionEvidence {
  run: AssetCashFlowProjectionRun;
  terms: AssetTermsVersion | null;
}

/** Asset Operations runs have their own identities; they are not contractual scenario runs. */
export function CashLadderProjectionEvidence({ row, onClose }: { row: CashLadderDrillRow; onClose: () => void }) {
  const [result, setResult] = useState<{ row: CashLadderDrillRow; evidence?: ProjectionEvidence; error?: string } | null>(null);
  const state = result?.row === row ? result : null;

  useEffect(() => {
    const controller = new AbortController();
    getAssetOperations(row.securityId, { signal: controller.signal, allowDevelopmentFallback: false })
      .then((detail) => {
        if (controller.signal.aborted) return;
        const run = detail.cashFlowProjectionRuns.find((candidate) =>
          candidate.projectionRunId === row.projectionRunIdentity && candidate.securityId === row.securityId);
        if (detail.subject.securityId !== row.securityId || !run ||
          (row.projectionEngineVersion !== "—" && run.engineVersion !== row.projectionEngineVersion)) {
          setResult({ row, error: "The source no longer exposes this exact projection run. Refresh the ladder or inspect the instrument; a newer run cannot substitute for this contribution's evidence." });
          return;
        }
        const terms = row.termsVersionId || row.termsHash
          ? detail.termsHistory.find((candidate) => candidate.securityId === row.securityId &&
            (!row.termsVersionId || candidate.termsVersionId === row.termsVersionId) &&
            (!row.termsHash || candidate.termsHash === row.termsHash)) ?? null
          : null;
        setResult({ row, evidence: { run, terms } });
      })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) setResult({ row, error: describeApiError(failure, "Projection evidence could not be loaded. Check access to the instrument and retry.").summary });
      });
    return () => controller.abort();
  }, [row]);

  return (
    <section aria-label={`Projection evidence for ${row.instrument}`} className="mt-4 space-y-3 rounded border border-border p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-foreground">Projection evidence · {row.instrument}</h3>
          <p className="mt-1 text-xs text-muted-foreground">This contribution's exact run and terms are checked against the authoritative instrument record.</p>
        </div>
        <Button type="button" size="sm" variant="outline" onClick={onClose}>Close projection evidence</Button>
      </div>
      <dl className="grid gap-2 break-all text-xs sm:grid-cols-2">
        <div><dt className="font-semibold">Projection run</dt><dd className="font-mono">{row.projectionRunIdentity}</dd></div>
        <div><dt className="font-semibold">Ladder contribution</dt><dd>{row.flowType} · {row.dueDate} · {row.amountLabel} {row.currency}</dd></div>
        <div><dt className="font-semibold">Terms version</dt><dd className="font-mono">{row.termsVersionId ?? "Not supplied"}</dd></div>
        <div><dt className="font-semibold">Terms hash</dt><dd className="font-mono">{row.termsHash ?? "Not supplied"}</dd></div>
        <div><dt className="font-semibold">Flow source</dt><dd>{row.sourceDomain} · {row.sourceEntityId ?? "Identity unavailable"}</dd></div>
      </dl>
      {state?.error ? <StatusBanner tone="warning" role="alert" title="Exact projection evidence unavailable" detail={state.error} /> : null}
      {!state?.error && !state?.evidence ? <p role="status" className="text-xs text-muted-foreground">Loading authoritative projection evidence…</p> : null}
      {state?.evidence ? (
        <>
          <dl className="grid gap-2 break-all text-xs sm:grid-cols-2">
            <div><dt className="font-semibold">Run status</dt><dd>{state.evidence.run.status}</dd></div>
            <div><dt className="font-semibold">Engine</dt><dd>{state.evidence.run.engineVersion}</dd></div>
            <div><dt className="font-semibold">Projection as of</dt><dd>{state.evidence.run.projectionAsOf}</dd></div>
            <div><dt className="font-semibold">Generated</dt><dd>{state.evidence.run.generatedAt}</dd></div>
            <div><dt className="font-semibold">Run source</dt><dd>{state.evidence.run.sourceDomain} · {state.evidence.run.sourceEntityId}</dd></div>
          </dl>
          {state.evidence.terms ? (
            <div className="space-y-1 text-xs">
              <h4 className="font-semibold">Matched terms evidence</h4>
              <p>{state.evidence.terms.summary}</p>
              <p>Version {state.evidence.terms.versionNumber} · effective {state.evidence.terms.effectiveDate} · recorded {state.evidence.terms.recordedAt}</p>
              <p className="break-all">{state.evidence.terms.sourceDomain} · {state.evidence.terms.sourceEntityId}</p>
            </div>
          ) : <StatusBanner tone="warning" title="Exact terms evidence unavailable" detail="The source did not return terms matching this contribution's version and hash. Current terms are not a substitute." />}
        </>
      ) : null}
      <Link className="inline-block text-xs underline underline-offset-2" to={workstationRouteWithQuery("accountingSecurityMaster", { securityId: row.securityId, tab: "evidence" })}>Open instrument evidence in Security Master</Link>
    </section>
  );
}
