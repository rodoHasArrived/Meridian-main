import { ArrowUpRight, FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { ReportingHubModel, ReportingHubTone } from "@/lib/reporting-hub";

export interface ReportingHubProps {
  model: ReportingHubModel;
  className?: string;
}

const toneClasses: Record<ReportingHubTone, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

/**
 * Report-first overview for the reporting workspace: one card per template family
 * answering "is this current, and where do I open it?" — the question most users
 * bring to a reporting tool, distinct from the production queue below.
 */
export function ReportingHub({ model, className }: ReportingHubProps) {
  if (model.isEmpty) {
    return null;
  }

  return (
    <section role="region" aria-label="Report hub" className={className}>
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="eyebrow-label">Report hub</div>
              <CardTitle>Reports by family</CardTitle>
              <CardDescription>
                Find a report, confirm it is current, and open the latest output without scanning the run queue.
              </CardDescription>
            </div>
            <Badge variant={model.attentionCount > 0 ? "warning" : "success"}>{model.summaryLabel}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {model.cards.map((card) => (
              <li
                key={card.family}
                aria-label={card.ariaLabel}
                className="flex h-full flex-col gap-2 rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
              >
                <div className="flex items-start justify-between gap-2">
                  <span className="min-w-0 break-words font-semibold text-foreground">{card.family}</span>
                  <Badge variant={card.badgeVariant}>{card.statusLabel}</Badge>
                </div>
                <p className={cn("text-xs leading-5", toneClasses[card.statusTone])}>{card.approvedAsOfLabel}</p>
                <p className="text-[11px] leading-4 text-muted-foreground">
                  {card.detail}
                  {card.latestRunId ? (
                    <>
                      {" · latest "}
                      <span className="font-mono">{card.latestAsOfLabel}</span>
                    </>
                  ) : null}
                </p>
                <div className="mt-auto pt-1">
                  {card.openHref ? (
                    <Button asChild variant="outline" size="sm">
                      <a href={card.openHref} aria-label={`${card.openLabel} for ${card.family}`}>
                        <FileText className="h-4 w-4" aria-hidden="true" />
                        {card.openLabel}
                        <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </a>
                    </Button>
                  ) : (
                    <span className="text-[11px] italic text-muted-foreground">No output to open yet</span>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>
    </section>
  );
}
