import { useCallback, useEffect, useState } from "react";
import { PauseCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SegmentedControl } from "@/components/ui/segmented-control";
import {
  describeReviewQueueFailure,
  discardSecurityMasterHeldAmendment,
  listSecurityMasterHeldAmendments,
  type SecurityMasterHeldAmendment,
  type SecurityMasterHeldAmendmentStatus
} from "@/lib/api/security-master-review-queues.api";

/**
 * Held amendments (Security Master review A2): a lower-ranked source tried to overwrite a value a
 * higher-ranked source supplied, so the whole amendment was held. The operator adopts the held value
 * through the conflict's governed resolution (open the security, resolve the named conflict, which stages
 * a draft) or discards the row. The browser decides nothing about ranking; rows are shown as served.
 */
export interface SecurityMasterHeldAmendmentsPanelProps {
  load?: (status: SecurityMasterHeldAmendmentStatus) => Promise<SecurityMasterHeldAmendment[]>;
  discard?: (row: SecurityMasterHeldAmendment) => Promise<unknown>;
  onOpenSecurity?: (securityId: string) => void;
}

type PanelState =
  | { status: "loading" }
  | { status: "ready"; rows: SecurityMasterHeldAmendment[] }
  | { status: "error"; message: string };

const STATUSES: SecurityMasterHeldAmendmentStatus[] = ["Held", "Replayed", "Discarded"];

const defaultLoad = (status: SecurityMasterHeldAmendmentStatus) => listSecurityMasterHeldAmendments({ status });
const defaultDiscard = (row: SecurityMasterHeldAmendment) => discardSecurityMasterHeldAmendment(row.heldAmendmentId);

export function formatHeldAt(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `${date.toISOString().slice(0, 16).replace("T", " ")} UTC`;
}

export function SecurityMasterHeldAmendmentsPanel({ load, discard, onOpenSecurity }: SecurityMasterHeldAmendmentsPanelProps) {
  const [filter, setFilter] = useState<SecurityMasterHeldAmendmentStatus>("Held");
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [reloadCount, setReloadCount] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    (load ?? defaultLoad)(filter)
      .then((rows) => {
        if (!cancelled) setState({ status: "ready", rows });
      })
      .catch((error: unknown) => {
        if (!cancelled) setState({ status: "error", message: describeReviewQueueFailure(error, "view held Security Master amendments") });
      });
    return () => {
      cancelled = true;
    };
  }, [load, filter, reloadCount]);

  const onDiscard = useCallback(
    async (row: SecurityMasterHeldAmendment) => {
      setBusyId(row.heldAmendmentId);
      setNotice(null);
      setActionError(null);
      try {
        await (discard ?? defaultDiscard)(row);
        setNotice(`Discarded the held amendment from ${row.sourceSystem}.`);
      } catch (error: unknown) {
        setActionError(describeReviewQueueFailure(error, "discard held Security Master amendments"));
      } finally {
        setBusyId(null);
        // A refused discard (409) means the row was replayed or discarded elsewhere; reload either way.
        setReloadCount((count) => count + 1);
      }
    },
    [discard]
  );

  const noun = filter.toLowerCase();

  return (
    <Card className="panel-surface" aria-label="Held Security Master amendments">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <PauseCircle className="h-4 w-4 text-primary" aria-hidden="true" />
          Held amendments
        </CardTitle>
        <CardDescription className="mt-2">
          A lower-ranked source tried to overwrite a value a higher-ranked source supplied, so its amendment was held. Open the
          security and resolve the named conflict to adopt the held value, or discard the row.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <SegmentedControl
          size="sm"
          options={STATUSES}
          value={filter}
          onChange={(value) => {
            setNotice(null);
            setActionError(null);
            setFilter(value as SecurityMasterHeldAmendmentStatus);
          }}
          aria-label="Held amendment status"
        />
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
            Loading held amendments…
          </p>
        )}
        {state.status === "error" && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {state.message}
          </div>
        )}
        {state.status === "ready" && state.rows.length === 0 && <p className="text-sm text-muted-foreground">No {noun} amendments.</p>}
        {state.status === "ready" && state.rows.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Held amendment rows">
              <thead>
                <tr className="text-left text-muted-foreground">
                  <th scope="col" className="py-1 pr-3 font-medium">Source</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Dominated fields</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Conflicts</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Held at</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Version</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Status</th>
                  <th scope="col" className="py-1 font-medium">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {state.rows.map((row) => (
                  <tr key={row.heldAmendmentId} className="border-t border-border/60">
                    <td className="py-2 pr-3">{row.sourceSystem}</td>
                    <td className="py-2 pr-3 font-mono text-xs">
                      {row.dominatedFieldPaths.length === 0 ? "—" : row.dominatedFieldPaths.join(", ")}
                    </td>
                    <td className="py-2 pr-3">{row.conflictIds.length}</td>
                    <td className="py-2 pr-3">{formatHeldAt(row.heldAt)}</td>
                    <td className="py-2 pr-3">{row.heldAtVersion}</td>
                    <td className="py-2 pr-3">{row.status}</td>
                    <td className="py-2">
                      <div className="flex gap-2">
                        {onOpenSecurity && (
                          <Button size="sm" variant="outline" onClick={() => onOpenSecurity(row.securityId)}>
                            Open security
                          </Button>
                        )}
                        {row.status === "Held" && (
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={busyId === row.heldAmendmentId}
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
