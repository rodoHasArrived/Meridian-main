/**
 * Execution audit trail panel for Trading → Risk.
 *
 * `GET /api/execution/audit/search` exposes the cross-object action trail with
 * actor, outcome, correlation, and hash-chain fields. Nothing in the workstation
 * called it; the flat `/api/execution/audit` list had no caller either. This panel
 * is the operator surface for both.
 */

import { useCallback, useEffect, useState } from "react";
import { RefreshCcw, ScrollText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { searchExecutionAuditTrail } from "@/lib/api/execution-audit.api";
import {
  buildAuditTrailPanelViewModel,
  type AuditTrailTone
} from "@/screens/trading-screen.audit-trail.view-model";
import type { AuditTrailExplorerResult } from "@/types/execution-audit.types";

const AUDIT_LIMIT = 50;

export function ExecutionAuditTrailPanel() {
  const [result, setResult] = useState<AuditTrailExplorerResult | null>(null);
  const [searchText, setSearchText] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setResult(await searchExecutionAuditTrail({
        searchText: appliedSearch || undefined,
        limit: AUDIT_LIMIT
      }));
    } catch (reason) {
      setResult(null);
      setError(reason instanceof Error ? reason.message : "The audit trail could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [appliedSearch]);

  useEffect(() => { void refresh(); }, [refresh]);

  const vm = buildAuditTrailPanelViewModel(result, AUDIT_LIMIT);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Governed actions</div>
            <CardTitle className="flex items-center gap-2">
              <ScrollText className="h-5 w-5 text-primary" />
              Execution audit trail
            </CardTitle>
            <CardDescription>
              Every recorded order, control, and operator action with its actor, outcome, and
              action-ledger chain position.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {error ? <StatusBanner tone="danger" title="Audit trail unavailable" detail={error} /> : null}
        {vm.truncationNotice ? (
          <StatusBanner tone="warning" title="Results truncated" detail={vm.truncationNotice} />
        ) : null}

        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(event) => { event.preventDefault(); setAppliedSearch(searchText.trim()); }}
        >
          <label className="flex flex-1 flex-col gap-1 text-xs uppercase tracking-wide text-muted-foreground">
            Filter
            <Input
              aria-label="Audit trail search text"
              placeholder="Symbol, actor, run, correlation, or reason"
              value={searchText}
              onChange={(event) => setSearchText(event.target.value)}
            />
          </label>
          <Button size="sm" type="submit" disabled={loading}>
            {loading ? "Searching…" : "Search"}
          </Button>
          <div className="text-xs text-muted-foreground">
            <span className="font-mono">{vm.countLabel}</span> entries · as of{" "}
            <span className="font-mono">{vm.asOfLabel}</span>
          </div>
        </form>

        <table className="w-full text-sm" aria-label="Execution audit trail entries">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="py-2">Occurred</th>
              <th>Object</th>
              <th>Action</th>
              <th>Outcome</th>
              <th>Actor</th>
              <th>Context</th>
              <th>Action ledger</th>
            </tr>
          </thead>
          <tbody>
            {vm.rows.length === 0 ? (
              <tr>
                <td colSpan={7} className="py-4 text-center text-muted-foreground">
                  {loading ? "Loading audit trail…" : vm.emptyState}
                </td>
              </tr>
            ) : vm.rows.map((row) => (
              <tr key={row.auditId} className="border-t border-border/60" aria-label={row.ariaLabel}>
                <td className="py-2 font-mono text-xs">{row.occurredAt}</td>
                <td className="font-mono text-xs">{row.objectLabel}</td>
                <td>{row.actionLabel}</td>
                <td><Badge variant={badgeVariant(row.outcomeTone)}>{row.outcome}</Badge></td>
                <td>{row.actor}</td>
                <td className="max-w-[24rem] break-words text-xs text-muted-foreground">{row.context}</td>
                <td><Badge variant={badgeVariant(row.ledgerTone)}>{row.ledgerLabel}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function badgeVariant(tone: AuditTrailTone): "default" | "success" | "warning" | "danger" {
  return tone === "default" ? "default" : tone;
}
