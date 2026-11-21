import type { ComponentProps, ReactNode } from "react";
import { Link } from "react-router-dom";
import type { OperationsContinuityWorkflow } from "@/types";
import { CloseBoard } from "@/components/meridian/close-board";
import { CloseChecklistProgressChart } from "@/components/meridian/workstation-evidence-charts";
import { CloseChecklistHistoryChart } from "@/components/meridian/close-checklist-history-chart";
import { PrepareNextPeriodPanel } from "./accounting-screen.prepare-next-period";
import { FiscalYearClosePanel } from "./accounting-screen.fiscal-year-close";
import { BookFxSettlementPanel } from "./accounting-screen.book-fx-settlement";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

/** Accounting Today retains casework; governed close review has one destination. */
export function AccountingCloseLanding({ caseWorkbench, closeWorkflow, search }: {
  caseWorkbench: ReactNode; closeWorkflow: OperationsContinuityWorkflow | null; search: string;
}) {
  return <>
    {caseWorkbench}
    <PrepareNextPeriodPanel initialWorkflowId={closeWorkflow?.workflowId} />
    <Card className="panel-surface"><CardHeader><CardTitle>Period close</CardTitle><CardDescription>Review the complete blocker list, checklist, approvals and evidence in the Close hub.</CardDescription></CardHeader>
      <CardContent className="flex flex-wrap gap-2"><Button asChild><Link to={{ pathname: "/accounting/close", search }}>Open Close hub</Link></Button>
        <Button asChild variant="outline"><Link to={{ pathname: "/accounting/workflow", search, hash: "#close-command-center" }}>Valuation and period operations</Link></Button></CardContent>
    </Card>
  </>;
}

/** Preserve the newer period-preparation capabilities on the dedicated operating route. */
export function AccountingPeriodPreparation({ closeWorkflow, ledgerBookId, closeBoard }: {
  closeWorkflow: OperationsContinuityWorkflow | null; ledgerBookId?: string;
  closeBoard?: ComponentProps<typeof CloseBoard>["board"] | null;
}) {
  return <>
    {closeWorkflow?.closeChecklist ? <section aria-label="Close task progress" className="space-y-2"><h3 className="text-sm font-semibold">Close task progress</h3><CloseChecklistProgressChart tasks={closeWorkflow.closeChecklist} /></section> : null}
    {closeBoard ? <CloseBoard board={closeBoard} /> : null}
    <PrepareNextPeriodPanel initialWorkflowId={closeWorkflow?.workflowId} />
    <FiscalYearClosePanel initialBookId={ledgerBookId} />
    <BookFxSettlementPanel />
    {closeWorkflow ? <CloseChecklistHistoryChart workflowId={closeWorkflow.workflowId} workflowVersion={closeWorkflow.version} /> : null}
  </>;
}
