import { Badge } from "@/components/ui/badge";
import type { MarkFreshnessPresentation } from "@/lib/mark-freshness";

export function MarkFreshnessCell({ mark }: { mark: MarkFreshnessPresentation }) {
  return <div title={mark.reason}>
    <Badge variant={mark.tone}>{mark.label}</Badge>
    <div className="mt-1 text-xs text-muted-foreground">Observed {mark.observedOn} · age {mark.age}</div>
  </div>;
}
