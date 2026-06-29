import { useEffect, useState } from "react";
import { getSecurityTrustSnapshot } from "@/lib/api";
import { SecurityPassportEditor, type SecurityPassportWorkbenchService } from "./security-passport-editor";

/**
 * Loads the governed-write concurrency token (the passport version) for a security, then mounts the
 * {@link SecurityPassportEditor}. The version is not carried by the coverage/search read models, so it is
 * fetched from the trust snapshot on open; a stale-version reload re-fetches it.
 */
export interface SecurityPassportEditorLauncherProps {
  securityId: string;
  symbol: string;
  assetClass?: string | null;
  trustPosture?: string | null;
  fundProfileId?: string | null;
  service?: Partial<SecurityPassportWorkbenchService>;
  /** Injectable version loader; defaults to reading the trust snapshot's economic-definition version. */
  loadVersion?: (securityId: string) => Promise<number>;
}

async function defaultLoadVersion(securityId: string): Promise<number> {
  const snapshot = await getSecurityTrustSnapshot(securityId);
  return snapshot?.economicDefinition?.version ?? 0;
}

type LauncherState =
  | { status: "loading" }
  | { status: "ready"; version: number }
  | { status: "error"; message: string };

export function SecurityPassportEditorLauncher({
  securityId,
  symbol,
  assetClass,
  trustPosture,
  fundProfileId,
  service,
  loadVersion
}: SecurityPassportEditorLauncherProps) {
  const [state, setState] = useState<LauncherState>({ status: "loading" });
  // Bumping this re-runs the effect (and its cleanup) instead of calling the loader imperatively, so a
  // pending request is always cancelled before the next one — no state updates on an unmounted component.
  const [reloadCount, setReloadCount] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    const loader = loadVersion ?? defaultLoadVersion;
    loader(securityId)
      .then((version) => {
        if (!cancelled) setState({ status: "ready", version });
      })
      .catch((error) => {
        if (!cancelled) {
          setState({ status: "error", message: error instanceof Error ? error.message : "Failed to load the passport version." });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [securityId, loadVersion, reloadCount]);

  if (state.status === "loading") {
    return (
      <p className="text-sm text-muted-foreground" role="status">
        Loading passport for {symbol}…
      </p>
    );
  }

  if (state.status === "error") {
    return (
      <p className="text-sm text-[var(--danger)]" role="alert">
        {state.message}
      </p>
    );
  }

  return (
    <SecurityPassportEditor
      securityId={securityId}
      symbol={symbol}
      assetClass={assetClass}
      version={state.version}
      trustPosture={trustPosture}
      fundProfileId={fundProfileId}
      service={service}
      onReloadRequested={() => setReloadCount((count) => count + 1)}
    />
  );
}
