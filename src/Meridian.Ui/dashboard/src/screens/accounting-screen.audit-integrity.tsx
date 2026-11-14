import { TableWorkbench } from "@/components/meridian/table-workbench";
/**
 * Audit-chain integrity check for Accounting → Configuration.
 *
 * `GET /api/ledger/audit-integrity` verifies every retained accounting-action
 * and ledger-event hash chain without appending anything. It walks each chain
 * end to end, so it runs on demand rather than on every screen load.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { getAuditIntegrityReport } from "@/lib/api/audit-integrity.api";
import { isAbortError, isApiError } from "@/lib/api-errors";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import {
  buildAuditIntegrityViewModel,
  type AuditIntegrityTone,
  type AuditIntegrityViewModel
} from "@/screens/accounting-screen.audit-integrity.view-model";

export function AuditChainIntegrityPanel() {
  const [view, setView] = useState<AuditIntegrityViewModel | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ title: string; detail: string } | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  // Leaving the screen abandons the run: the server stops scanning and releases the chain-head
  // locks and its single-flight slot instead of finishing for a result nobody will read.
  useEffect(() => () => inFlight.current?.abort(), []);

  const verify = useCallback(async () => {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy(true);
    setError(null);
    setNotice(null);
    // A fresh check was asked for, so the previous verdict no longer stands while it runs.
    setView(null);
    try {
      setView(buildAuditIntegrityViewModel(await getAuditIntegrityReport({ signal: controller.signal })));
    } catch (reason) {
      if (isAbortError(reason) || controller.signal.aborted) {
        return;
      }
      setView(null);
      // Refusals are not verification failures: say why nothing was checked instead of raising an alarm.
      if (isApiError(reason) && reason.status === 403) {
        setNotice({
          title: "Requires maintenance authority",
          detail: "Audit chain verification spans every tenant's records, so it is limited to operators with AdminMaintenance."
        });
      } else if (isApiError(reason) && reason.status === 409) {
        setNotice({
          title: "Verification already running",
          detail: "Another audit chain verification is in progress. Retry when it completes."
        });
      } else if (isApiError(reason) && reason.status === 429) {
        setNotice({
          title: "Rate limited",
          detail: "The workstation's shared request budget is exhausted. Retry in a moment."
        });
      } else {
        setError(reason instanceof Error ? reason.message : "Audit integrity check failed.");
      }
    } finally {
      if (inFlight.current === controller) {
        inFlight.current = null;
        setBusy(false);
      }
    }
  }, []);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Governance</div>
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              Audit chain integrity
            </CardTitle>
            <CardDescription>
              Re-verifies the hash-chained accounting and ledger audit logs end to end. Nothing is appended or repaired.
            </CardDescription>
          </div>
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => void verify()}
            disabled={busy}
            aria-label="Verify the accounting and ledger audit chains"
          >
            <ShieldCheck className="mr-2 h-4 w-4" />
            {busy ? "Verifying…" : "Verify audit chains"}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {error ? <StatusBanner role="alert" tone="danger" title="Audit integrity check failed" detail={error} /> : null}
        {notice ? <StatusBanner role="status" tone="info" title={notice.title} detail={notice.detail} /> : null}
        {view ? (
          <>
            <StatusBanner
              role="status"
              tone={view.tone === "default" ? "warning" : view.tone}
              title={view.title}
              detail={view.verdict}
            />
            <TableWorkbench<(typeof view.rows)[number]> workbenchId="accounting-audit-integrity" sourceKey={JSON.stringify(view.rows)} rows={view.rows} columns={[{id:"field-0",label:"Chain",rowHeader:true,exportValue:row=>row.label,render:row=><>{row.label}</>},
{id:"field-1",label:"Status",exportValue:row=>row.statusLabel,render:row=><>
                      <Badge variant={badgeVariant(row.tone)}>{row.statusLabel}</Badge>
                    </>},
{id:"field-2",label:"Coverage",exportValue:row=>[row.coverage,row.failure].filter(Boolean).join("; "),render:row=><>
                      <div>{row.coverage}</div>
                      {row.failure ? <div className="font-mono text-danger">{row.failure}</div> : null}
                    </>}]} getRowId={row=>row.chain} getSearchText={row=>[row.label,row.statusLabel,[row.coverage,row.failure].filter(Boolean).join("; ")].join(" ")} completePopulation sourceDescription="Server-computed chain verification snapshot; coverage and failures are retained verbatim." ariaLabel="Audit chain verification results" emptyText="No source records returned." />
            <p className="text-xs text-muted-foreground">Verification ran {formatDateTimeLabel(view.verifiedAtUtc)}.</p>
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}

function badgeVariant(tone: AuditIntegrityTone): "default" | "success" | "warning" | "danger" {
  return tone;
}
