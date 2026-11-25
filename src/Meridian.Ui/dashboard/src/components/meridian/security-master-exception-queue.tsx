import { useCallback, useEffect, useState } from "react";
import { ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SegmentedControl } from "@/components/ui/segmented-control";
import { formatRevisionAge } from "@/components/meridian/security-master-review-inbox";
import {
  assignSecurityMasterExceptionCase,
  describeExceptionSla,
  describeReviewQueueFailure,
  listSecurityMasterExceptionCases,
  syncSecurityMasterExceptionCases,
  type SecurityMasterExceptionCaseQueueItem,
  type SecurityMasterExceptionCaseStatus,
  type SecurityMasterExceptionSyncResult
} from "@/lib/api/security-master-review-queues.api";

/**
 * The deployment-global Security Master exception queue (review D2): conflicts, identifier ambiguities and
 * data-quality violations with SLA due dates and assignment. Sync asks the server to reconcile open cases
 * with current conditions. The SLA verdict and age are the server's and are never recomputed here.
 */
export interface SecurityMasterExceptionQueuePanelProps {
  load?: (status: SecurityMasterExceptionCaseStatus) => Promise<SecurityMasterExceptionCaseQueueItem[]>;
  sync?: () => Promise<SecurityMasterExceptionSyncResult>;
  assign?: (caseId: string, assignee: string) => Promise<unknown>;
  onOpenSecurity?: (securityId: string) => void;
  /** Pre-fills the assignee, e.g. with the signed-in operator. */
  defaultAssignee?: string;
}

type PanelState =
  | { status: "loading" }
  | { status: "ready"; rows: SecurityMasterExceptionCaseQueueItem[] }
  | { status: "error"; message: string };

const STATUSES: SecurityMasterExceptionCaseStatus[] = ["Open", "Closed"];

const defaultLoad = (status: SecurityMasterExceptionCaseStatus) => listSecurityMasterExceptionCases(status);

export function SecurityMasterExceptionQueuePanel({
  load,
  sync,
  assign,
  onOpenSecurity,
  defaultAssignee
}: SecurityMasterExceptionQueuePanelProps) {
  const [filter, setFilter] = useState<SecurityMasterExceptionCaseStatus>("Open");
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [reloadCount, setReloadCount] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [selectedCaseId, setSelectedCaseId] = useState<string | null>(null);
  const [assignee, setAssignee] = useState(defaultAssignee?.trim() ?? "");

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    (load ?? defaultLoad)(filter)
      .then((rows) => {
        if (!cancelled) setState({ status: "ready", rows });
      })
      .catch((error: unknown) => {
        if (!cancelled) setState({ status: "error", message: describeReviewQueueFailure(error, "view the Security Master exception queue") });
      });
    return () => {
      cancelled = true;
    };
  }, [load, filter, reloadCount]);

  const rows = state.status === "ready" ? state.rows : [];
  const selected = rows.find((item) => item.row.caseId === selectedCaseId) ?? null;
  const breached = rows.filter((item) => item.slaBreached).length;
  const canAssign = !busy && selected !== null && selected.row.status === "Open" && assignee.trim().length > 0;

  const onSync = useCallback(async () => {
    setBusy(true);
    setNotice(null);
    setActionError(null);
    try {
      const result = await (sync ?? syncSecurityMasterExceptionCases)();
      setNotice(`Sync opened ${result.opened}, refreshed ${result.refreshed} and closed ${result.closed} case(s).`);
      setReloadCount((count) => count + 1);
    } catch (error: unknown) {
      setActionError(describeReviewQueueFailure(error, "sync the Security Master exception queue"));
    } finally {
      setBusy(false);
    }
  }, [sync]);

  const onAssign = useCallback(async () => {
    if (!selected || selected.row.status !== "Open" || !assignee.trim()) return;
    const name = assignee.trim();
    setBusy(true);
    setNotice(null);
    setActionError(null);
    try {
      await (assign ?? assignSecurityMasterExceptionCase)(selected.row.caseId, name);
      setNotice(`Assigned "${selected.row.title}" to ${name}.`);
    } catch (error: unknown) {
      setActionError(describeReviewQueueFailure(error, "assign Security Master exception cases"));
    } finally {
      setBusy(false);
      // A 409 means the case closed meanwhile; reload so the queue shows it.
      setReloadCount((count) => count + 1);
    }
  }, [assign, assignee, selected]);

  const noun = filter.toLowerCase();

  return (
    <Card className="panel-surface" aria-label="Security Master exception queue">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ShieldAlert className="h-4 w-4 text-primary" aria-hidden="true" />
          Exception queue
        </CardTitle>
        <CardDescription className="mt-2">
          Conflicts, identifier ambiguities and data-quality violations across every security, with SLA due dates and assignment.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex flex-wrap items-end gap-3">
          <SegmentedControl
            size="sm"
            options={STATUSES}
            value={filter}
            onChange={(value) => {
              setNotice(null);
              setActionError(null);
              setSelectedCaseId(null);
              setFilter(value as SecurityMasterExceptionCaseStatus);
            }}
            aria-label="Exception case status"
          />
          <Button
            size="sm"
            variant="outline"
            disabled={busy}
            onClick={() => void onSync()}
            title="Open, refresh and close cases to match current conflicts, ambiguities and violations."
          >
            Sync cases
          </Button>
          <div className="space-y-1">
            <Label htmlFor="sm-exception-assignee">Assign selected case to</Label>
            <Input id="sm-exception-assignee" value={assignee} onChange={(event) => setAssignee(event.target.value)} />
          </div>
          <Button size="sm" disabled={!canAssign} onClick={() => void onAssign()}>
            Assign
          </Button>
        </div>
        {notice && (
          <p role="status" className="text-sm text-muted-foreground">
            {notice}
          </p>
        )}
        {actionError && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {actionError}
          </div>
        )}
        {state.status === "loading" && (
          <p role="status" className="text-sm text-muted-foreground">
            Loading exception cases…
          </p>
        )}
        {state.status === "error" && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {state.message}
          </div>
        )}
        {state.status === "ready" && rows.length === 0 && <p className="text-sm text-muted-foreground">No {noun} cases.</p>}
        {state.status === "ready" && rows.length > 0 && (
          <>
            <p className="text-sm text-muted-foreground">
              {rows.length} {noun} case(s); {breached} past SLA.
            </p>
            <div className="overflow-x-auto">
              <table className="w-full text-sm" aria-label="Exception case rows">
                <thead>
                  <tr className="text-left text-muted-foreground">
                    <th scope="col" className="py-1 pr-3 font-medium">
                      <span className="sr-only">Select</span>
                    </th>
                    <th scope="col" className="py-1 pr-3 font-medium">Severity</th>
                    <th scope="col" className="py-1 pr-3 font-medium">Category</th>
                    <th scope="col" className="py-1 pr-3 font-medium">Case</th>
                    <th scope="col" className="py-1 pr-3 font-medium">Assignee</th>
                    <th scope="col" className="py-1 pr-3 font-medium">SLA</th>
                    <th scope="col" className="py-1 pr-3 font-medium">Age</th>
                    <th scope="col" className="py-1 font-medium">
                      <span className="sr-only">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((item) => (
                    <tr key={item.row.caseId} className="border-t border-border/60 align-top">
                      <td className="py-2 pr-3">
                        <input
                          type="radio"
                          name="sm-exception-case"
                          aria-label={`Select ${item.row.title}`}
                          checked={selectedCaseId === item.row.caseId}
                          onChange={() => setSelectedCaseId(item.row.caseId)}
                        />
                      </td>
                      <td className="py-2 pr-3">{item.row.severity}</td>
                      <td className="py-2 pr-3">{item.row.category}</td>
                      <td className="py-2 pr-3">
                        <div>{item.row.title}</div>
                        <div className="text-xs text-muted-foreground">{item.row.detail}</div>
                      </td>
                      <td className="py-2 pr-3">{item.row.assignee || "Unassigned"}</td>
                      <td className={item.slaBreached ? "py-2 pr-3 text-danger" : "py-2 pr-3"}>{describeExceptionSla(item)}</td>
                      <td className="py-2 pr-3">{formatRevisionAge(item.ageHours)}</td>
                      <td className="py-2">
                        {onOpenSecurity && item.row.securityId && (
                          <Button size="sm" variant="outline" onClick={() => onOpenSecurity(item.row.securityId as string)}>
                            Open security
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
