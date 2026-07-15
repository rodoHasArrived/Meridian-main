import { FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ReportingCommandStatusView, type ReportingCommandStatus } from "@/screens/reporting-screen.shared-components";
import type { ReportingStarterKitPanelViewModel } from "@/screens/reporting-screen.view-model";

interface ReportingStarterKitChooserProps {
  panel: ReportingStarterKitPanelViewModel;
  status: ReportingCommandStatus | null;
  runningStarterKitId: string | null;
  onProvision: (kitId: string, title: string) => void | Promise<void>;
}

export function ReportingStarterKitChooser({
  panel,
  status,
  runningStarterKitId,
  onProvision
}: ReportingStarterKitChooserProps) {
  if (!panel.hasCards) {
    return null;
  }

  return (
    <section role="region" aria-label={panel.title} className="panel-surface space-y-4 px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">Starter desk</div>
          <h2 className="mt-2 text-lg font-semibold text-foreground">{panel.title}</h2>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{panel.description}</p>
        </div>
        <Badge variant={panel.statusVariant}>{panel.statusLabel}</Badge>
      </div>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {panel.cards.map((card) => {
          const busyId = `starter-kit:${card.id}`;
          return (
            <Card key={card.id} className="border-border/70 bg-background/45">
              <CardHeader className="space-y-2">
                <div className="flex flex-wrap items-center gap-1.5">
                  <Badge variant="outline">{card.defaultPeriodLabel}</Badge>
                  <Badge variant="outline">{card.seedScheduleSummary}</Badge>
                </div>
                <CardTitle className="text-base">{card.title}</CardTitle>
                <CardDescription>{card.description}</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                <dl className="grid gap-2 text-xs sm:grid-cols-2">
                  <div className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2">
                    <dt className="uppercase text-muted-foreground">Templates</dt>
                    <dd className="mt-1 font-medium text-foreground">{card.templateSummary}</dd>
                  </div>
                  <div className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2">
                    <dt className="uppercase text-muted-foreground">Hub layout</dt>
                    <dd className="mt-1 break-all font-mono text-foreground">{card.layoutLabel}</dd>
                  </div>
                </dl>
                <div className="flex flex-wrap gap-1.5" aria-label={`${card.title} templates`}>
                  {card.templateNames.map((name) => (
                    <Badge key={name} variant="outline">{name}</Badge>
                  ))}
                </div>
                <div className="space-y-1.5" aria-label={`${card.title} draft schedules`}>
                  {card.seedSchedules.map((schedule) => (
                    <div key={schedule.id} className="rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs">
                      <div className="flex flex-wrap items-center gap-1.5">
                        <Badge variant="outline">{schedule.cadence}</Badge>
                        <span className="font-medium text-foreground">{schedule.stateLabel}</span>
                      </div>
                      <p className="mt-1 text-muted-foreground">{schedule.description}</p>
                      <p className="mt-1 font-mono text-xs text-muted-foreground">{schedule.deliveryTargetSummary}</p>
                    </div>
                  ))}
                </div>
                <Button
                  type="button"
                  size="sm"
                  className="w-full justify-center"
                  aria-label={card.actionAriaLabel}
                  busy={runningStarterKitId === busyId}
                  busyLabel="Provisioning"
                  disabled={Boolean(runningStarterKitId)}
                  onClick={() => void onProvision(card.id, card.title)}
                >
                  <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                  {card.actionLabel}
                </Button>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {status ? <ReportingCommandStatusView status={status} /> : null}
    </section>
  );
}
