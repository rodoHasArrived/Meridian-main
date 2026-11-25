import { useCallback, useEffect, useState } from "react";
import { Inbox } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  classifyWorkbenchWriteError,
  discardSecurityMasterRevision,
  listPendingSecurityMasterRevisions,
  type SecurityMasterPendingRevision
} from "@/lib/api/security-master-workbench.api";

/**
 * Review inbox (Security Master review P5/D1): governed revisions awaiting action across every security,
 * oldest first. Under the Institutional profile the approver must differ from the drafter, so a reviewer
 * needs to find a colleague's submitted draft without opening each passport. Opening a row hands the
 * security to the passport workbench; discarding withdraws an abandoned revision.
 */
export interface SecurityMasterReviewInboxPanelProps {
  load?: () => Promise<SecurityMasterPendingRevision[]>;
  discard?: (row: SecurityMasterPendingRevision, reason: string) => Promise<unknown>;
  onOpenSecurity?: (securityId: string, revisionId: string) => void;
}

type InboxState =
  | { status: "loading" }
  | { status: "ready"; rows: SecurityMasterPendingRevision[] }
  | { status: "error"; message: string };

const defaultLoad = () => listPendingSecurityMasterRevisions(["Draft", "Submitted", "Approved"]);
const defaultDiscard = (row: SecurityMasterPendingRevision, reason: string) =>
  discardSecurityMasterRevision(row.securityId, { revisionId: row.revisionId, reason });

export function formatRevisionAge(ageHours: number): string {
  if (ageHours < 1) return "under 1h";
  if (ageHours < 48) return `${Math.round(ageHours)}h`;
  return `${Math.round(ageHours / 24)}d`;
}

export function SecurityMasterReviewInboxPanel({ load, discard, onOpenSecurity }: SecurityMasterReviewInboxPanelProps) {
  const [state, setState] = useState<InboxState>({ status: "loading" });
  const [reloadCount, setReloadCount] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [busyRevisionId, setBusyRevisionId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    (load ?? defaultLoad)()
      .then((rows) => {
        if (!cancelled) setState({ status: "ready", rows });
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setState({ status: "error", message: error instanceof Error ? error.message : "The review inbox could not be loaded." });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [load, reloadCount]);

  const onDiscard = useCallback(
    async (row: SecurityMasterPendingRevision) => {
      setBusyRevisionId(row.revisionId);
      setNotice(null);
      try {
        await (discard ?? defaultDiscard)(row, "Discarded from the review inbox.");
        setNotice(`Discarded revision on ${row.fieldPath ?? "the security"}.`);
        setReloadCount((count) => count + 1);
      } catch (error: unknown) {
        const classified = classifyWorkbenchWriteError(error);
        setNotice("message" in classified && classified.message ? classified.message : "The revision could not be discarded.");
      } finally {
        setBusyRevisionId(null);
      }
    },
    [discard]
  );

  return (
    <Card className="panel-surface" aria-label="Security Master review inbox">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Inbox className="h-4 w-4 text-primary" aria-hidden="true" />
          Review inbox
        </CardTitle>
        <CardDescription className="mt-2">
          Revisions awaiting submission, approval or publication across every security, oldest first.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {notice && (
          <p role="status" className="text-sm text-muted-foreground">
            {notice}
          </p>
        )}
        {state.status === "loading" && (
          <p role="status" className="text-sm text-muted-foreground">
            Loading pending revisions…
          </p>
        )}
        {state.status === "error" && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {state.message}
          </div>
        )}
        {state.status === "ready" && state.rows.length === 0 && (
          <p className="text-sm text-muted-foreground">No revisions are awaiting action.</p>
        )}
        {state.status === "ready" && state.rows.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Pending Security Master revisions">
              <thead>
                <tr className="text-left text-muted-foreground">
                  <th scope="col" className="py-1 pr-3 font-medium">Field</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Proposed value</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Drafted by</th>
                  <th scope="col" className="py-1 pr-3 font-medium">State</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Waiting</th>
                  <th scope="col" className="py-1 font-medium">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {state.rows.map((row) => (
                  <tr key={row.revisionId} className="border-t border-border/60">
                    <td className="py-2 pr-3 font-mono text-xs">{row.fieldPath ?? "—"}</td>
                    <td className="py-2 pr-3">{row.fieldValue ?? "—"}</td>
                    <td className="py-2 pr-3">{row.actor}</td>
                    <td className="py-2 pr-3">{row.state}</td>
                    <td className="py-2 pr-3">{formatRevisionAge(row.ageHours)}</td>
                    <td className="py-2">
                      <div className="flex gap-2">
                        {onOpenSecurity && (
                          <Button size="sm" variant="outline" onClick={() => onOpenSecurity(row.securityId, row.revisionId)}>
                            Open
                          </Button>
                        )}
                        {(row.state === "Draft" || row.state === "Submitted") && (
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={busyRevisionId === row.revisionId}
                            onClick={() => void onDiscard(row)}
                          >
                            Discard
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
