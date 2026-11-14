import { SectionHeading } from "@/components/meridian/section-heading";
import type {
  AccountingTaskModeViewModel,
  AccountingWorkstream
} from "@/screens/accounting-screen.task-mode-view-model";
import type { WorkspaceSummary } from "@/types";
import { SectionDescription } from "@/components/ui/section-description";

export interface AccountingChipViewModel {
  label: string;
  value: string;
}

export interface AccountingWorkbenchContextProps {
  workspace: WorkspaceSummary;
  workstream: AccountingWorkstream;
  taskMode: AccountingTaskModeViewModel;
}

export function AccountingWorkbenchContext({
  workspace,
  workstream,
  taskMode
}: AccountingWorkbenchContextProps) {
  return (
    <section
      id="accounting-overview"
      role="region"
      aria-label={`${workspace.label} workbench context`}
      data-workstream={workstream}
      data-task-mode={taskMode.id}
      className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1"
    >
      <SectionHeading as="h2" title={taskMode.title} description={taskMode.description}
        helpId={`accounting-${taskMode.id}`} headingClassName="text-base" />
    </section>
  );
}

export function AccountingChip({ label, value }: AccountingChipViewModel) {
  return (
    <span className="toolbar-chip" role="group" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono capitalize text-foreground">{value}</span>
    </span>
  );
}
