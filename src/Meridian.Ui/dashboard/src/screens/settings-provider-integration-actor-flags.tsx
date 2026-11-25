import { useCallback, useEffect, useState } from "react";
import { UserX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { describeApiError } from "@/lib/api-errors";
import {
  listProviderIntegrationActorFlags,
  scanProviderIntegrationActorFlags,
  type ProviderIntegrationActorFlag,
  type ProviderIntegrationActorFlagScanResult
} from "@/lib/api/provider-integration-actor-flags.api";

/**
 * Provider-integration records stored before the server bound actors to the signed-in operator may name a
 * session desk label ("Meridian Operator", "<strategy> Desk") or a Settings placeholder instead of a person.
 * Those records are never rewritten; an append-only flag marks each one as actor-unverified.
 */
export interface ProviderIntegrationActorFlagsPanelProps {
  load?: () => Promise<ProviderIntegrationActorFlag[]>;
  scan?: () => Promise<ProviderIntegrationActorFlagScanResult>;
}

type PanelState =
  | { status: "loading" }
  | { status: "ready"; flags: ProviderIntegrationActorFlag[] }
  | { status: "error"; message: string };

function errorText(error: unknown, fallback: string): string {
  const described = describeApiError(error, fallback);
  return described.summary;
}

export function ProviderIntegrationActorFlagsPanel({ load, scan }: ProviderIntegrationActorFlagsPanelProps) {
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [reloadCount, setReloadCount] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    (load ?? (() => listProviderIntegrationActorFlags()))()
      .then((flags) => {
        if (!cancelled) setState({ status: "ready", flags });
      })
      .catch((error: unknown) => {
        if (!cancelled) setState({ status: "error", message: errorText(error, "Actor flags could not be loaded.") });
      });
    return () => {
      cancelled = true;
    };
  }, [load, reloadCount]);

  const onScan = useCallback(async () => {
    setScanning(true);
    setNotice(null);
    setScanError(null);
    try {
      const result = await (scan ?? (() => scanProviderIntegrationActorFlags()))();
      setNotice(
        `Checked ${result.actorFieldsScanned} actor field(s): ${result.flagsAppended} newly flagged, ${result.alreadyFlagged} already flagged.`
      );
      setReloadCount((count) => count + 1);
    } catch (error: unknown) {
      setScanError(errorText(error, "The scan could not be completed."));
    } finally {
      setScanning(false);
    }
  }, [scan]);

  return (
    <section className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3" aria-label="Unverified provider-integration actors">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="grid gap-1">
          <h3 className="flex items-center gap-2 text-sm font-medium">
            <UserX className="h-4 w-4 text-primary" aria-hidden="true" />
            Unverified actors
          </h3>
          <p className="text-sm text-muted-foreground">
            Approvals, reviews and saves recorded before actors were tied to the signed-in operator may name a desk
            label instead of a person. They are flagged here, never rewritten. Treat a flagged approval or review as
            unattributed and repeat it if the decision still matters.
          </p>
        </div>
        <Button size="sm" variant="outline" busy={scanning} disabled={scanning} onClick={() => void onScan()}>
          Scan for unverified actors
        </Button>
      </div>
      {notice ? (
        <p role="status" className="text-sm text-muted-foreground">
          {notice}
        </p>
      ) : null}
      {scanError ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {scanError}
        </div>
      ) : null}
      {state.status === "loading" ? (
        <p role="status" className="text-sm text-muted-foreground">
          Loading actor flags…
        </p>
      ) : null}
      {state.status === "error" ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {state.message}
        </div>
      ) : null}
      {state.status === "ready" && state.flags.length === 0 ? (
        <p className="text-sm text-muted-foreground">No unverified actors are flagged.</p>
      ) : null}
      {state.status === "ready" && state.flags.length > 0 ? (
        <div className="overflow-x-auto">
          <table className="w-full text-sm" aria-label="Unverified actor flags">
            <thead>
              <tr className="text-left text-muted-foreground">
                <th scope="col" className="py-1 pr-3 font-medium">Record</th>
                <th scope="col" className="py-1 pr-3 font-medium">Field</th>
                <th scope="col" className="py-1 pr-3 font-medium">Recorded as</th>
                <th scope="col" className="py-1 pr-3 font-medium">Why</th>
                <th scope="col" className="py-1 pr-3 font-medium">When</th>
                <th scope="col" className="py-1 font-medium">Flagged by</th>
              </tr>
            </thead>
            <tbody>
              {state.flags.map((flag) => (
                <tr key={flag.flagId} className="border-t border-border/60 align-top">
                  <td className="py-2 pr-3">
                    <div>{flag.subjectId ?? flag.recordKind}</div>
                    <div className="text-xs text-muted-foreground">{flag.recordKind}</div>
                  </td>
                  <td className="py-2 pr-3 font-mono text-xs">{flag.fieldPath}</td>
                  <td className="py-2 pr-3">{flag.recordedValue.trim() ? flag.recordedValue : "(blank)"}</td>
                  <td className="py-2 pr-3">{flag.reason}</td>
                  <td className="py-2 pr-3">{flag.recordedAt ?? "—"}</td>
                  <td className="py-2">{flag.flaggedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
