import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { CaseInspector, CaseVariance } from "./case-inspector";
import type { ReconciliationBreakQueueItem } from "@/types";
import type { ReconciliationBreakRowViewModel } from "./accounting-screen.view-model";

export function AccountingCaseWorkbench({ breakRows, caseRecords, selectedBreakId, onSelectBreak, onCaseChanged }: {
  breakRows: ReconciliationBreakRowViewModel[];
  caseRecords: ReconciliationBreakQueueItem[];
  selectedBreakId: string | null;
  onSelectBreak: (breakId: string) => void;
  onCaseChanged: (item: ReconciliationBreakQueueItem) => void;
}) {
  const selectedId = selectedBreakId ?? breakRows[0]?.breakId ?? null;
  return <section id="accounting-case-workbench" className="workspace-section-band" aria-labelledby="accounting-case-workbench-heading">
    <div className="workspace-section-subheader"><div className="min-w-0"><p className="eyebrow-label">Casework</p>
      <h3 id="accounting-case-workbench-heading" className="workspace-section-title">Accounting case workbench</h3>
      <p className="workspace-section-summary">Select a case to review its retained comparison and record a version-checked decision.</p></div></div>
    <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(15rem,0.7fr)_minmax(0,1.7fr)]">
      <Card className="panel-surface min-w-0" role="region" aria-label="Accounting case queue"><CardHeader><CardTitle className="text-base">Cases · {breakRows.length}</CardTitle><CardDescription>All loaded cases in the current scope.</CardDescription></CardHeader>
        <CardContent><div role="list" aria-label="Accounting case queue rows" className="grid gap-2">
          {breakRows.map((row) => <div role="listitem" key={row.breakId}><button type="button" aria-pressed={row.breakId === selectedId}
            className={`block w-full min-w-0 break-words rounded border px-3 py-2 text-left text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary ${row.breakId === selectedId ? "border-primary bg-primary/10" : "border-border"}`}
            onClick={() => onSelectBreak(row.breakId)}><span className="block font-semibold">{row.financeLabel}</span><span className="block">{row.strategyName}</span><span className="block font-mono"><CaseVariance item={row} /></span><span className="block">{row.status} · {row.ownerLabel}</span></button></div>)}
          {!breakRows.length ? <p role="status" className="text-sm text-muted-foreground">No cases loaded in this scope.</p> : null}
        </div></CardContent></Card>
      <Card className="panel-surface-strong min-w-0" role="region" aria-label="Selected accounting case"><CardContent className="p-4">
        <CaseInspector key={selectedId} caseId={selectedId} initialCase={caseRecords.find((item) => item.breakId === selectedId)} onChanged={onCaseChanged} />
      </CardContent></Card>
    </div>
  </section>;
}
