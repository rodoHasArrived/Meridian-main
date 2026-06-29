import { useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import type { SecurityMasterEntry } from "@/types";
import { buildCoverageSecurityDrillIns } from "./coverage-passport-drill-in.view-model";
import { SecurityPassportEditorLauncher } from "./security-passport-editor-launcher";
import type { SecurityPassportWorkbenchService } from "./security-passport-editor";

/**
 * Drill-through from a multi-asset coverage row (an asset-class aggregate) to the governed passport
 * editor: lists the Security Master records of that asset class and launches the editor for the chosen
 * one. Renders nothing when no securities of the asset class are loaded.
 */
export interface CoveragePassportDrillInProps {
  assetClass: string;
  securities: readonly SecurityMasterEntry[] | null | undefined;
  service?: Partial<SecurityPassportWorkbenchService>;
  loadVersion?: (securityId: string) => Promise<number>;
}

export function CoveragePassportDrillIn({ assetClass, securities, service, loadVersion }: CoveragePassportDrillInProps) {
  const [expanded, setExpanded] = useState(false);
  const [activeSecurityId, setActiveSecurityId] = useState<string | null>(null);

  const rows = useMemo(() => buildCoverageSecurityDrillIns(securities, assetClass), [securities, assetClass]);
  if (rows.length === 0) {
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
        Edit passports ({rows.length})
      </Button>

      {expanded ? (
        <div className="mt-1 space-y-1">
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
        </div>
      ) : null}
    </div>
  );
}
