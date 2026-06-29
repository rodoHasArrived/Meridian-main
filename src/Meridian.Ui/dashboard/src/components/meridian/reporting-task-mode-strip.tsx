import { FileText, Landmark, Network, PencilLine, RotateCcw } from "lucide-react";
import { Link } from "react-router-dom";
import type { ReportingTaskMode, ReportingTaskModeId } from "@/lib/reporting-task-modes";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export function ReportingTaskModeStrip({ modes }: { modes: ReportingTaskMode[] }) {
  return (
    <nav aria-label="Reporting task modes" className="panel-surface-strong px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">Task modes</div>
          <h2 className="mt-2 text-lg font-semibold leading-tight text-foreground">Focused Reporting routes</h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Choose the smallest Reporting lane for the decision in front of you: cockpit triage, build, run status,
            delivery evidence, exports, or governance.
          </p>
        </div>
        <Badge variant={modes.some((mode) => mode.recommended) ? "warning" : "success"} dot>
          {modes.some((mode) => mode.recommended) ? "Queue guidance active" : "No queue guidance"}
        </Badge>
      </div>
      <ul className="mt-4 grid gap-2 md:grid-cols-2 xl:grid-cols-3" role="list">
        {modes.map((mode) => (
          <li
            key={mode.id}
            className={cn(
              "rounded-md border px-3 py-3",
              mode.active
                ? "border-primary/45 bg-primary/10"
                : "border-border/70 bg-secondary/20"
            )}
          >
            <div className="flex min-w-0 items-start justify-between gap-3">
              <Button asChild variant={mode.active ? "outline" : "ghost"} size="sm" className="min-w-0 justify-start">
                <Link
                  to={mode.href}
                  aria-current={mode.active ? "page" : undefined}
                  aria-label={`Open ${mode.label} reporting mode. ${mode.detail}`}
                >
                  <ReportingTaskModeIcon id={mode.id} />
                  <span className="truncate">{mode.label}</span>
                </Link>
              </Button>
              <span className="flex shrink-0 flex-wrap justify-end gap-1.5">
                {mode.recommended ? <Badge variant="warning">Recommended</Badge> : null}
                <Badge variant={mode.badgeVariant}>{mode.statusLabel}</Badge>
              </span>
            </div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{mode.detail}</p>
            <div className="mt-3 flex flex-wrap gap-1.5">
              <Badge variant="outline">{mode.countLabel}</Badge>
              {mode.active ? <Badge variant="default">Current mode</Badge> : null}
            </div>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function ReportingTaskModeIcon({ id }: { id: ReportingTaskModeId }) {
  const className = "h-4 w-4 shrink-0";
  switch (id) {
    case "report-builder":
      return <PencilLine className={className} aria-hidden="true" />;
    case "run-status":
      return <RotateCcw className={className} aria-hidden="true" />;
    case "delivery-evidence":
      return <Landmark className={className} aria-hidden="true" />;
    case "exports":
      return <FileText className={className} aria-hidden="true" />;
    case "governance":
      return <Network className={className} aria-hidden="true" />;
    default:
      return <FileText className={className} aria-hidden="true" />;
  }
}
