import { Network } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import type {
  ReportingChipViewModel,
  ReportingTaskModeViewModel,
  ReportingWorkbenchAction
} from "@/screens/reporting-screen.view-model";

export interface ReportingWorkbenchContextProps {
  taskMode: ReportingTaskModeViewModel;
  actions: ReportingWorkbenchAction[];
  chips: ReportingChipViewModel[];
}

export function ReportingWorkbenchContext({
  taskMode,
  actions,
  chips
}: ReportingWorkbenchContextProps) {
  return (
    <section
      role="region"
      aria-label="Reporting workbench context"
      className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
    >
      <div className="min-w-0">
        <div className="eyebrow-label">Reporting lane</div>
        <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
          {taskMode.label}
        </h2>
        <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{taskMode.description}</p>
      </div>
      <div className="flex flex-wrap items-center justify-end gap-2">
        <ReportingChip label="Task mode" value={taskMode.label} />
        <span className="sr-only">{taskMode.description}</span>
        {actions.map((action) => (
          <Button key={action.id} asChild variant="outline" size="sm">
            <Link to={action.href} aria-label={action.ariaLabel}>
              <Network className="h-4 w-4" aria-hidden="true" />
              {action.label}
            </Link>
          </Button>
        ))}
        {chips.map((chip) => (
          <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
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
