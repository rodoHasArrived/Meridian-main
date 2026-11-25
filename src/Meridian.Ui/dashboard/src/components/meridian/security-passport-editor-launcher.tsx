import { useEffect, useState } from "react";
import { getSecurityTrustSnapshot } from "@/lib/api";
import { listPendingSecurityMasterRevisions } from "@/lib/api/security-master-workbench.api";
import { SecurityPassportEditor, type SecurityPassportWorkbenchService } from "./security-passport-editor";
import { attachPendingDrafts, toPassportConflicts, type PassportConflictInput } from "./security-passport-editor.view-model";

/**
 * Loads what the {@link SecurityPassportEditor} needs, then mounts it: the governed-write concurrency
 * token (the passport version), the open source conflicts, and the pending "Use B" draft each conflict
 * already waits on, so a reload still shows those conflicts as awaiting their draft. The version and the
 * conflicts come from one trust-snapshot read; a stale-version reload re-fetches both.
 */
export interface SecurityPassportEditorLauncherProps {
  securityId: string;
  symbol: string;
  assetClass?: string | null;
  trustPosture?: string | null;
  fundProfileId?: string | null;
  service?: Partial<SecurityPassportWorkbenchService>;
  /** Injectable version loader. When given without `loadPassport`, the editor opens with no conflict list. */
  loadVersion?: (securityId: string) => Promise<number>;
  /** Injectable loader for the version, conflicts and any notice; defaults to the trust snapshot plus pending drafts. */
  loadPassport?: (securityId: string) => Promise<LoadedPassport>;
}

export interface LoadedPassport {
  version: number;
  conflicts: PassportConflictInput[];
  /** Shown above the editor when part of the context could not be loaded. */
  notice?: string | null;
}

export async function defaultLoadPassport(securityId: string): Promise<LoadedPassport> {
  const snapshot = await getSecurityTrustSnapshot(securityId);
  const version = snapshot?.economicDefinition?.version ?? 0;
  const conflicts = toPassportConflicts(snapshot?.conflictAssessments);
  if (conflicts.length === 0) {
    return { version, conflicts };
  }
  try {
    const pending = await listPendingSecurityMasterRevisions(["Draft", "Submitted", "Approved"], undefined, securityId);
    return { version, conflicts: attachPendingDrafts(conflicts, pending) };
  } catch {
    // The conflicts are still worth showing; the server refuses a second "Use B" on a field that already
    // has an open draft, so a missing marker cannot double-stage.
    return {
      version,
      conflicts,
      notice: "Pending drafts could not be loaded, so conflicts that already wait on a draft are not marked."
    };
  }
}

type LauncherState =
  | { status: "loading" }
  | { status: "ready"; passport: LoadedPassport }
  | { status: "error"; message: string };

export function SecurityPassportEditorLauncher({
  securityId,
  symbol,
  assetClass,
  trustPosture,
  fundProfileId,
  service,
  loadVersion,
  loadPassport
}: SecurityPassportEditorLauncherProps) {
  const [state, setState] = useState<LauncherState>({ status: "loading" });
  // Bumping this re-runs the effect (and its cleanup) instead of calling the loader imperatively, so a
  // pending request is always cancelled before the next one — no state updates on an unmounted component.
  const [reloadCount, setReloadCount] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    const loader =
      loadPassport ??
      (loadVersion
        ? (id: string) => loadVersion(id).then((version): LoadedPassport => ({ version, conflicts: [] }))
        : defaultLoadPassport);
    loader(securityId)
      .then((passport) => {
        if (!cancelled) setState({ status: "ready", passport });
      })
      .catch((error) => {
        if (!cancelled) {
          setState({ status: "error", message: error instanceof Error ? error.message : "Failed to load the passport version." });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [securityId, loadVersion, loadPassport, reloadCount]);

  if (state.status === "loading") {
    return (
      <p className="text-sm text-muted-foreground" role="status">
        Loading passport for {symbol}…
      </p>
    );
  }

  if (state.status === "error") {
    return (
      <p className="text-sm text-danger" role="alert">
        {state.message}
      </p>
    );
  }

  return (
    <>
      {state.passport.notice ? (
        <p className="text-sm text-muted-foreground" role="status">
          {state.passport.notice}
        </p>
      ) : null}
      <SecurityPassportEditor
        securityId={securityId}
        symbol={symbol}
        assetClass={assetClass}
        version={state.passport.version}
        trustPosture={trustPosture}
        fundProfileId={fundProfileId}
        conflicts={state.passport.conflicts}
        service={service}
        onReloadRequested={() => setReloadCount((count) => count + 1)}
      />
    </>
  );
}
