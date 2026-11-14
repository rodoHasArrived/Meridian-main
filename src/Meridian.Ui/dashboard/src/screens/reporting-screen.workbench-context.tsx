import type { ReactNode } from "react";
import { Network } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { SectionDescription } from "@/components/ui/section-description";
import { cn } from "@/lib/utils";
import type {
  ReportingChipViewModel,
  ReportingWorkbenchAction
} from "@/screens/reporting-screen.view-model";
import type { ReportingTaskModeViewModel } from "@/screens/reporting-screen.task-mode-view-model";

export interface ReportingWorkbenchContextProps {
  taskMode: ReportingTaskModeViewModel;
  actions: ReportingWorkbenchAction[];
  compact?: boolean;
  context?: ReactNode;
}

export function ReportingWorkbenchContext({ taskMode, actions, compact = false, context }: ReportingWorkbenchContextProps) {
  return (
    <section
      role="region"
      aria-label="Reporting workbench context"
      className="flex flex-wrap items-end justify-between gap-3"
    >
      <div className="min-w-0 flex-1">
        <div className={cn("flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1", compact && "sr-only")}>
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {taskMode.title}
          </h2>
          {!compact ? (
            <SectionDescription descriptionKey={`reporting-${taskMode.id}`} label={`About ${taskMode.title}`}>
              {taskMode.description}
            </SectionDescription>
          ) : null}
        </div>
        {context}
      </div>
      <div className="flex flex-wrap items-center justify-end gap-2">
        {actions.map((action) => (
          <Button key={action.id} asChild variant="outline" size="sm">
            <Link to={action.href} aria-label={action.ariaLabel}>
              <Network className="h-4 w-4" aria-hidden="true" />
              {action.label}
            </Link>
          </Button>
        ))}
      </div>
    </section>
  );
}

export function ReportingChip({ label, value }: ReportingChipViewModel) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}
