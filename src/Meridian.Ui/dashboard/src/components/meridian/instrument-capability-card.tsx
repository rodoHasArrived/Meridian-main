import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { InstrumentCapabilityAssessment } from "@/types";

export function InstrumentCapabilityCard({ capabilities }: { capabilities: InstrumentCapabilityAssessment[] }) {
  const panelId = useId();
  const [selection, setSelection] = useState<{ capability: string; scope: string } | null>(null);
  const scope = capabilities.map((item) => `${item.capability}:${item.securityVersion}:${item.asOfUtc}`).join("|");
  const selected = selection?.scope === scope ? capabilities.find((item) => item.capability === selection.capability) : null;
  return (
    <section aria-label="Instrument capabilities" className="space-y-2">
      <div>
        <h3 className="text-sm font-semibold">Instrument capabilities</h3>
        <p className="text-xs text-muted-foreground">Readiness for the named operation and selected definition. Approval and posting retain their own checks.</p>
      </div>
      {capabilities.length === 0 ? <p className="text-sm text-muted-foreground">Capability assessment is unavailable. Refresh the passport to evaluate this security.</p> : (
        <div role="group" aria-label="Capability status" className="grid grid-cols-3 gap-2">
          {capabilities.map((item) => {
            const applicable = item.applicability !== "NotApplicable";
            const label = applicable ? item.status : "Not applicable";
            const variant = !applicable ? "outline" : item.status === "Ready" ? "success" : item.status === "Blocked" ? "danger" : "warning";
            return (
              <button key={item.capability} type="button" aria-controls={panelId}
                aria-label={`${item.label}: ${label}`}
                aria-expanded={selected?.capability === item.capability}
                onClick={() => setSelection(selected?.capability === item.capability ? null : { capability: item.capability, scope })}
                className={cn("flex min-w-0 flex-col items-start gap-2 rounded border border-border p-2 text-left text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
                  selected?.capability === item.capability && "border-primary bg-primary/5")}>
                  <span className="font-medium">{item.label}</span>
                  <Badge variant={variant}>{label}</Badge>
              </button>
            );
          })}
        </div>
      )}
      <div id={panelId} hidden={!selected} role="region" aria-label={selected ? `${selected.label} capability details` : "Capability details"}
        className="space-y-2 rounded border border-border p-3 text-xs">
        {selected ? <>
          <h4 className="font-semibold">{selected.label} · {selected.applicability === "NotApplicable" ? "Not applicable" : selected.status}</h4>
          <p className="text-muted-foreground">{selected.context}</p>
          <p className="text-muted-foreground">Version {selected.securityVersion ?? "unavailable"} · {selected.asOfUtc} · {selected.evaluationState === "RequiresContext" ? "Context required" : selected.evaluationState}</p>
          {selected.reasons.length === 0 ? <p>{selected.applicability === "NotApplicable" ? "This operation is outside the declared instrument behavior." : "Passive checks passed for this operation."}</p> : selected.reasons.map((reason, index) => (
            <div key={`${reason.code}-${index}`} className="space-y-1">
              <p>{reason.reason}</p>
              {reason.remediationPath.startsWith("/") && !reason.remediationPath.startsWith("//") ? (
                <Link className="font-medium text-primary underline underline-offset-2" to={reason.remediationPath}>Review {selected.label.toLowerCase()} evidence</Link>
              ) : null}
            </div>
          ))}
        </> : null}
      </div>
    </section>
  );
}
