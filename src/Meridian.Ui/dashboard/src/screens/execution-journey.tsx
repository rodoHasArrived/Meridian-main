import { useEffect, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { searchExecutionAuditTrail } from "@/lib/api/execution-audit.api";
import { buildExecutionJourney } from "@/screens/execution-journey.view-model";
import { auditLedgerLabel } from "@/screens/trading-screen.audit-trail.view-model";
import type { AuditTrailExplorerResult } from "@/types/execution-audit.types";
import type { TradingFill } from "@/types";

const EMPTY_FILLS: TradingFill[] = [];

export function ExecutionJourneyPanel({ orderId, fills = EMPTY_FILLS }: { orderId: string; fills?: TradingFill[] }) {
  const [read, setRead] = useState<{ orderId: string; results?: AuditTrailExplorerResult[]; error?: string } | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    Promise.all([
      searchExecutionAuditTrail({ objectId: orderId, limit: 1000 }, { signal: controller.signal, allowDevelopmentFallback: false }),
      searchExecutionAuditTrail({ relatedObjectId: orderId, limit: 1000 }, { signal: controller.signal, allowDevelopmentFallback: false })
    ]).then((results) => {
      if (current) setRead({ orderId, results });
    }).catch((error: unknown) => {
      if (current) setRead({ orderId, error: error instanceof Error ? error.message : "Execution evidence unavailable." });
    });
    return () => { current = false; controller.abort(); };
  }, [orderId, revision]);
  const scopeRead = read?.orderId === orderId ? read : null;
  const vm = buildExecutionJourney(orderId, scopeRead?.results ?? [], fills);
  return <Card aria-label={`Execution journey for ${orderId}`}>
    <CardHeader>
      <div className="flex items-start justify-between gap-2"><CardTitle className="text-base">Execution journey · {orderId}</CardTitle><Button size="sm" variant="outline" onClick={() => { setRead(null); setRevision((value) => value + 1); }}>Refresh journey</Button></div>
      <CardDescription>Chronological retained evidence. Recorded times describe audit observations; fill times describe occurrence.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-3 text-sm">
      {!scopeRead ? <p role="status">Loading recorded order evidence…</p> : null}
      {scopeRead?.error ? <p role="alert">Audit source unavailable: {scopeRead.error}. The journey is incomplete.</p> : null}
      {vm.sourceUnavailable ? <p role="status">Execution audit source is not configured. The journey is incomplete.</p> : null}
      {vm.sourceUnknown ? <p>Audit source availability was not reported; completeness is unknown.</p> : null}
      {vm.truncated ? <p role="status">Results truncated: earlier events may be missing from this journey.</p> : null}
      {!vm.hasControlEvidence ? <p>No recorded control evidence in the loaded results. Control outcomes are unknown.</p> : null}
      <p className="text-xs text-muted-foreground">Fill coverage is limited to the loaded recent blotter; complete historical fill coverage was not supplied.</p>
      <p className="text-xs text-muted-foreground">Only explicit order relationships are included. Sequence jumps can reflect other orders; they do not establish missing events. {vm.asOf ? `Audit read as of ${vm.asOf}.` : ""}</p>
      <ol className="space-y-3 border-l border-border pl-4" aria-label={`Chronological events for ${orderId}`}>
        {vm.events.map((event) => <li key={event.id} className="relative">
          <span className="absolute -left-[1.3rem] top-1.5 h-2 w-2 rounded-full bg-primary" aria-hidden="true" />
          <details><summary className="cursor-pointer rounded-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"><span className="font-medium">{event.kind}: {event.label}</span><span className="block font-mono text-xs text-muted-foreground">{event.timestampKind} {event.timestamp}</span></summary>
            <dl className="mt-2 space-y-1 break-words text-xs">
              {event.audit ? <>
                <div><dt className="inline font-medium">Actor: </dt><dd className="inline">{event.audit.actor ?? "Unattributed / system"}</dd></div>
                <div><dt className="inline font-medium">Source / integrity: </dt><dd className="inline">{event.audit.actionLedgerSource ?? "Unreported"} · {auditLedgerLabel(event.audit)}</dd></div>
                <div><dt className="inline font-medium">Audit ID: </dt><dd className="inline">{event.audit.auditId}</dd></div>
                {event.audit.correlationId ? <div><dt className="inline font-medium">Correlation: </dt><dd className="inline">{event.audit.correlationId}</dd></div> : null}
                {event.audit.reason || event.audit.message ? <div><dt className="inline font-medium">Evidence: </dt><dd className="inline">{event.audit.reason} {event.audit.message}</dd></div> : null}
                {Object.entries(event.audit.metadata ?? {}).map(([key, value]) => <div key={key}><dt className="inline font-medium">{key}: </dt><dd className="inline">{value}</dd></div>)}
                {event.audit.evidenceRoute ? <div><dt className="inline font-medium">Retained evidence reference: </dt><dd className="inline font-mono">{event.audit.evidenceRoute}</dd></div> : null}
              </> : null}
              {event.fill ? <><div><dt className="inline font-medium">Fill ID: </dt><dd className="inline">{event.fill.fillId}</dd></div><div><dt className="inline font-medium">Venue: </dt><dd className="inline">{event.fill.venue}</dd></div><div><dt className="inline font-medium">Source: </dt><dd className="inline">Loaded trading blotter. Recording time unavailable.</dd></div></> : null}
            </dl>
          </details>
        </li>)}
      </ol>
      {scopeRead?.results && vm.events.length === 0 ? <p>No retained events were found for this order in the loaded sources.</p> : null}
    </CardContent>
  </Card>;
}
