import { useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { searchSecurities } from "@/lib/api";
import type { SecurityMasterEntry } from "@/types";
import { buildCoverageSecurityDrillIns } from "./coverage-passport-drill-in.view-model";
import { SecurityPassportEditorLauncher } from "./security-passport-editor-launcher";
import type { SecurityPassportWorkbenchService } from "./security-passport-editor";

const ASSET_CLASS_SECURITY_TAKE = 100;

/**
 * Default per-asset-class securities source. The coverage panel has no per-security read model, and the
 * Security Master search matches the asset-class text, so we query by the asset class and let
 * {@link buildCoverageSecurityDrillIns} keep only exact asset-class matches (dropping incidental name or
 * identifier hits). The result is a bounded launch list — not an exhaustive registry of the asset class.
 */
async function defaultLoadAssetClassSecurities(assetClass: string): Promise<readonly SecurityMasterEntry[]> {
  return searchSecurities(assetClass, ASSET_CLASS_SECURITY_TAKE, true);
}

/**
 * Drill-through from a multi-asset coverage row (an asset-class aggregate) to the governed passport
 * editor: lists the Security Master records of that asset class and launches the editor for the chosen
 * one.
 *
 * A coverage row carries no securityId, and the search read model is not pre-populated in the coverage
 * panel, so the drill-in lazily fetches its asset-class members the first time it is expanded. Callers
 * may instead pass an eager `securities` list (used by tests and any caller that already holds one); in
 * that mode the drill-in renders nothing when no securities of the asset class are present.
 */
export interface CoveragePassportDrillInProps {
  assetClass: string;
  /** Eager securities. When omitted, the drill-in lazily fetches its asset-class members on expand. */
  securities?: readonly SecurityMasterEntry[] | null;
  /** Injectable asset-class securities loader (defaults to the Security Master search). */
  loadSecurities?: (assetClass: string) => Promise<readonly SecurityMasterEntry[]>;
  service?: Partial<SecurityPassportWorkbenchService>;
  loadVersion?: (securityId: string) => Promise<number>;
}

export function CoveragePassportDrillIn({
  assetClass,
  securities,
  loadSecurities,
  service,
  loadVersion
}: CoveragePassportDrillInProps) {
  const isLazy = securities === undefined;
  const [expanded, setExpanded] = useState(false);
  const [activeSecurityId, setActiveSecurityId] = useState<string | null>(null);
  const [fetched, setFetched] = useState<readonly SecurityMasterEntry[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  // Tracks the asset class we have already requested, so toggling local loading state does not re-fetch
  // or cancel the in-flight request.
  const requestedAssetClassRef = useRef<string | null>(null);

  const load = loadSecurities ?? defaultLoadAssetClassSecurities;

  // Lazily fetch the asset-class securities the first time the drill-in is expanded.
  useEffect(() => {
    if (!isLazy || !expanded || requestedAssetClassRef.current === assetClass) {
      return;
    }

    requestedAssetClassRef.current = assetClass;
    let cancelled = false;
    setLoading(true);
    setFailed(false);
    load(assetClass)
      .then((result) => {
        if (!cancelled) {
          setFetched(result ?? []);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setFailed(true);
          setFetched([]);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [isLazy, expanded, assetClass, load]);

  const effectiveSecurities = isLazy ? fetched : securities;
  const rows = useMemo(
    () => buildCoverageSecurityDrillIns(effectiveSecurities, assetClass),
    [effectiveSecurities, assetClass]
  );

  // Eager mode with nothing to edit stays invisible, preserving the original contract.
  if (!isLazy && rows.length === 0) {
    return null;
  }

  const active = rows.find((row) => row.securityId === activeSecurityId) ?? null;

  return (
    <div className="mt-2">
      <Button
        type="button"
        size="sm"
        variant="ghost"
        aria-expanded={expanded}
        onClick={() => setExpanded((value) => !value)}
      >
        {rows.length > 0 ? `Edit passports (${rows.length})` : "Edit passports"}
      </Button>

      {expanded ? (
        <div className="mt-1 space-y-1">
          {loading ? (
            <p className="text-xs text-muted-foreground">Loading {assetClass} securities…</p>
          ) : failed ? (
            <p className="text-xs text-destructive">Could not load {assetClass} securities. Try again.</p>
          ) : rows.length === 0 ? (
            <p className="text-xs text-muted-foreground">No {assetClass} securities are available to edit.</p>
          ) : (
            <>
              <ul className="flex flex-wrap gap-1" aria-label={`${assetClass} passports`}>
                {rows.map((row) => (
                  <li key={row.securityId}>
                    <Button
                      type="button"
                      size="sm"
                      variant={row.securityId === activeSecurityId ? "secondary" : "outline"}
                      onClick={() => setActiveSecurityId((id) => (id === row.securityId ? null : row.securityId))}
                    >
                      {row.displayName}
                      {row.symbol ? ` · ${row.symbol}` : ""}
                    </Button>
                  </li>
                ))}
              </ul>

              {active ? (
                <div className="mt-2">
                  <SecurityPassportEditorLauncher
                    securityId={active.securityId}
                    symbol={active.symbol ?? active.displayName}
                    assetClass={active.assetClass}
                    service={service}
                    loadVersion={loadVersion}
                  />
                </div>
              ) : null}
            </>
          )}
        </div>
      ) : null}
    </div>
  );
}
