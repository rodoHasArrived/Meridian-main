import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { readinessToneToBadgeVariant } from "@/lib/shared-tone-mappings";
import type {
  EvidenceSlaAssessmentRowViewModel,
  EvidenceStatusTone
} from "@/screens/evidence-workbench-screen.view-model";

export function EvidenceSlaRows({ rows }: { rows: EvidenceSlaAssessmentRowViewModel[] }) {
  if (rows.length === 0) {
    return (
      <p className="rounded-sm border border-dashed border-border/70 bg-secondary/20 px-2.5 py-2 text-sm text-muted-foreground">
        No evidence SLA assessments returned.
      </p>
    );
  }

  return (
    <ul className="space-y-2" aria-label="Evidence SLA assessments">
      {rows.map((row) => (
        <li
          key={row.id}
          aria-label={row.ariaLabel}
          className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-semibold text-foreground">{row.policyLabel}</span>
            <Badge variant={readinessToneToBadgeVariant(row.tone)}>{row.breached ? "Breached" : "Fresh"}</Badge>
          </div>
          <div className="mt-2 flex flex-wrap gap-2">
            <Badge variant="outline">{row.evidenceKindLabel}</Badge>
            <Badge variant="outline">{row.ageLabel}</Badge>
            <Badge variant="outline">{row.freshnessLabel}</Badge>
            <Badge variant="outline">{row.severityLabel}</Badge>
          </div>
          <p className="mt-2 leading-5 text-muted-foreground">{row.message}</p>
          <TechnicalDetails label="Technical details" className="mt-2">
            <div className="break-all font-mono text-[0.7rem] text-muted-foreground">{row.evidenceId}</div>
          </TechnicalDetails>
        </li>
      ))}
    </ul>
  );
}

export function EvidenceList({
  title,
  items,
  tone,
  technical = false
}: {
  title: string;
  items: string[];
  tone: EvidenceStatusTone;
  technical?: boolean;
}) {
  const itemList = (
    <ul className="space-y-2 text-sm">
      {items.map((item) => (
        <li key={item} className="break-all rounded-md border border-border/70 bg-secondary/25 px-3 py-2 font-mono text-xs">
          {item}
        </li>
      ))}
    </ul>
  );

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-base">{title}</CardTitle>
          <Badge variant={readinessToneToBadgeVariant(tone)}>{items.length}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        {items.length > 0 ? (
          technical ? <TechnicalDetails label="Technical details">{itemList}</TechnicalDetails> : itemList
        ) : (
          <p className="text-sm text-muted-foreground">None.</p>
        )}
      </CardContent>
    </Card>
  );
}
