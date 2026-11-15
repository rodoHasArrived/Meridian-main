import { PageHeading } from "@/components/ui/page-heading";
import type {
  AccountingTaskModeViewModel,
  AccountingWorkstream
} from "@/screens/accounting-screen.task-mode-view-model";
import type { WorkspaceSummary } from "@/types";

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
      className="min-w-0"
    >
      <PageHeading title={taskMode.title} purpose={taskMode.description} descriptionKey={`accounting:${taskMode.id}`} />
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
