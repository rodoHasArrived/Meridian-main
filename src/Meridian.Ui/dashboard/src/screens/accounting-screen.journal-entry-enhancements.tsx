import { Search, X } from "lucide-react";
import type { KeyboardEvent as ReactKeyboardEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import type { ManualJournalEntryWorkbenchViewModel } from "@/screens/accounting-screen.view-model";

export function focusManualJournalCell(lineId: string, column: string): void {
  const existing = document.getElementById(`manual-je-${column}-${lineId}`);
  if (existing) {
    existing.focus();
    return;
  }
  window.requestAnimationFrame(() => document.getElementById(`manual-je-${column}-${lineId}`)?.focus());
}

export function handleManualJournalCellKeyDown(
  event: ReactKeyboardEvent<HTMLElement>,
  view: ManualJournalEntryWorkbenchViewModel,
  lineId: string,
  column: string,
  side?: "Debit" | "Credit"
): void {
  if (event.ctrlKey && event.key === "Enter") {
    event.preventDefault();
    focusManualJournalCell(view.insertLineAfter(lineId, side), column);
    return;
  }

  if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
      (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Enter")) {
    return;
  }

  const lineIndex = view.draft.lines.findIndex((line) => line.lineId === lineId);
  const targetLine = view.draft.lines[lineIndex + (event.key === "ArrowUp" ? -1 : 1)];
  if (!targetLine) {
    return;
  }

  event.preventDefault();
  view.selectLine(targetLine.lineId);
  focusManualJournalCell(targetLine.lineId, column);
}

export function focusManualJournalValidationTarget(
  view: ManualJournalEntryWorkbenchViewModel,
  targetId?: string | null
): void {
  if (targetId && view.draft.lines.some((line) => line.lineId === targetId)) {
    view.selectLine(targetId);
    window.requestAnimationFrame(() => {
      const target = document.getElementById(`manual-je-line-${targetId}`);
      target?.focus();
      target?.scrollIntoView?.({ block: "center", behavior: "smooth" });
    });
    return;
  }

  const target = targetId ? document.getElementById(`manual-je-header-${targetId}`) : null;
  const fallback = document.getElementById("manual-je-validation-heading");
  (target ?? fallback)?.focus();
  (target ?? fallback)?.scrollIntoView?.({ block: "center", behavior: "smooth" });
}

export function ManualJournalHealthBar({
  view,
  hasInvalidAmountEdits,
  invalidAmountReason
}: {
  view: ManualJournalEntryWorkbenchViewModel;
  hasInvalidAmountEdits: boolean;
  invalidAmountReason: string | null;
}) {
  return (
    <div className="accounting-journal-health-bar" role="region" aria-label="Journal health and actions">
      <div className="accounting-journal-health-summary">
        <span className={cn("accounting-journal-health-item", `is-${view.saveState}`)} role="status"><span aria-hidden="true" />{view.saveStatusLabel}</span>
        <span className={cn("accounting-journal-health-item", view.balanceStatusTone === "success" ? "is-saved" : "is-warning")}><span aria-hidden="true" />{view.balanceStatusLabel}</span>
        <span className={cn("accounting-journal-health-item", view.validationIsCurrent && view.blockingIssueCount === 0 ? "is-saved" : "is-warning")}><span aria-hidden="true" />{view.validationStatusLabel}</span>
        {view.blockingIssueCount > 0 ? <Button size="sm" variant="ghost" onClick={() => focusManualJournalValidationTarget(view, null)}>Show {view.blockingIssueCount} blocker{view.blockingIssueCount === 1 ? "" : "s"}</Button> : null}
      </div>
      <div className="accounting-journal-health-actions">
        <Button size="sm" variant="outline" busy={view.saveBusy} disabled={hasInvalidAmountEdits} disabledReason={hasInvalidAmountEdits ? invalidAmountReason : null} onClick={() => void view.save()}>Save draft</Button>
        <Button size="sm" variant="outline" busy={view.validateBusy} disabled={hasInvalidAmountEdits} disabledReason={hasInvalidAmountEdits ? invalidAmountReason : null} onClick={() => void view.validate()}>Validate</Button>
        <Button size="sm" busy={view.submitBusy} disabled={!view.canSubmit || hasInvalidAmountEdits} disabledReason={hasInvalidAmountEdits ? invalidAmountReason : view.submitDisabledReason} onClick={() => void view.submit()}>Submit approval</Button>
      </div>
      {view.recoveryStatusText ? (
        <div className="accounting-journal-recovery-status">
          <p>{view.recoveryStatusText}</p>
          {view.saveState === "recovered" ? <Button size="sm" variant="ghost" onClick={view.discardRecoveredDraft}>Discard recovered changes</Button> : null}
        </div>
      ) : null}
    </div>
  );
}

export function ManualJournalValidationNavigator({ view }: { view: ManualJournalEntryWorkbenchViewModel }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 id="manual-je-validation-heading" tabIndex={-1} className="text-sm font-semibold text-foreground">Validation navigator</h4>
        <Badge variant={view.validationIsCurrent ? "success" : "warning"} dot>{view.validationIsCurrent ? "Current" : "Required"}</Badge>
      </div>
      <div className="mt-3 space-y-2">
        {view.validationIssues.length > 0 ? view.validationIssues.map((issue) => (
          <div key={issue.id} className={cn("rounded border px-3 py-2 text-sm", issue.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : issue.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "border-border/70 bg-background/50 text-muted-foreground") }>
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="font-semibold">{issue.label}</div>
              <Badge variant={issue.tone === "danger" ? "danger" : issue.tone === "warning" ? "warning" : "outline"}>{issue.severity ?? "Review"}</Badge>
            </div>
            <div className="mt-1">{issue.message}</div>
            <div className="mt-1 text-xs">{issue.detail}</div>
            <Button size="sm" variant="ghost" className="mt-2" onClick={() => focusManualJournalValidationTarget(view, issue.targetId)}>{issue.targetId ? "Go to affected field" : "Review journal"}</Button>
          </div>
        )) : <p className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm text-muted-foreground">{view.validationIsCurrent ? "Validation is current for this draft and no issues were returned." : "Validation has not completed for the current draft. Use Validate before approval submission."}</p>}
      </div>
    </div>
  );
}

export function ManualJournalDimensionsInspector({ view }: { view: ManualJournalEntryWorkbenchViewModel }) {
  const selectedLine = view.draft.lines.find((line) => line.lineId === view.selectedLineId) ?? view.draft.lines[0] ?? null;
  const selectedAccountLabel = selectedLine ? view.accountOptions.find((option) => option.value === selectedLine.accountPath)?.label ?? "Not selected" : "Not selected";
  return (
    <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
      <h4 className="text-sm font-semibold text-foreground">Selected-line dimensions</h4>
      {selectedLine ? (
        <div className="mt-3 space-y-3 text-sm">
          <dl className="grid gap-2">
            <div><dt className="text-xs text-muted-foreground">GL account</dt><dd className="text-foreground">{selectedAccountLabel}</dd></div>
            <div><dt className="text-xs text-muted-foreground">Security</dt><dd className="break-all text-foreground">{selectedLine.securityDisplayName || "No Security Master reference"}</dd></div>
          </dl>
          <div className="grid gap-3 sm:grid-cols-2">
            <DimensionField label="Line entity" value={selectedLine.entityId ?? ""} placeholder={view.draft.entityId || "Inherit journal entity"} onChange={(value) => view.updateLine(selectedLine.lineId, { entityId: value })} />
            <DimensionField label="Fund allocation" value={selectedLine.fundAllocationId ?? ""} placeholder={view.draft.fundNodeId || "Optional"} onChange={(value) => view.updateLine(selectedLine.lineId, { fundAllocationId: value })} />
            <DimensionField label="Tax lot" value={selectedLine.taxLotId ?? ""} placeholder="Optional tax-lot ID" onChange={(value) => view.updateLine(selectedLine.lineId, { taxLotId: value })} />
            <DimensionField label="Strategy" value={view.draft.dimensions?.strategyId ?? ""} placeholder="Journal-level strategy" onChange={(value) => view.updateDraftDimensions({ strategyId: value })} />
            <DimensionField label="Portfolio" value={view.draft.dimensions?.portfolioId ?? ""} placeholder="Journal-level portfolio" onChange={(value) => view.updateDraftDimensions({ portfolioId: value })} />
            <DimensionField label="Cost center" value={view.draft.dimensions?.costCenterId ?? ""} placeholder="Journal-level cost center" onChange={(value) => view.updateDraftDimensions({ costCenterId: value })} />
          </div>
          <p className="text-xs text-muted-foreground">Blank line values inherit journal scope where available. Dimension edits invalidate the current validation and are autosaved as draft changes.</p>
          <TechnicalDetails label="Line system details">
            <dl className="grid gap-2 text-xs">
              <div><dt className="text-muted-foreground">Line ID</dt><dd className="mt-1 break-all font-mono text-foreground">{selectedLine.lineId}</dd></div>
              <div><dt className="text-muted-foreground">GL path</dt><dd className="mt-1 break-all font-mono text-foreground">{selectedLine.accountPath || "Not selected"}</dd></div>
              <div><dt className="text-muted-foreground">Security ID</dt><dd className="mt-1 break-all font-mono text-foreground">{selectedLine.securityId || "Not selected"}</dd></div>
            </dl>
          </TechnicalDetails>
          <div className="rounded border border-border/70 bg-background/50 p-2">
            <label className="space-y-1 text-sm">
              <span className="text-xs font-semibold uppercase text-muted-foreground">Security Master picker</span>
              <div className="flex gap-2">
                <input className="min-h-9 min-w-0 flex-1 rounded border border-border bg-background px-2" value={view.securitySearchQuery} placeholder="Ticker, ISIN, CUSIP, FIGI, name" onChange={(event) => view.updateSecuritySearchQuery(event.target.value)} />
                <Button size="icon" variant="outline" busy={view.securitySearchBusy} aria-label="Search Security Master" onClick={() => void view.searchSecurityMaster()}><Search className="h-3.5 w-3.5" aria-hidden="true" /></Button>
              </div>
            </label>
            <p role="status" className={cn("mt-2 text-xs", view.securitySearchErrorText ? "text-danger" : "text-muted-foreground")}>{view.securitySearchStatusText}</p>
            <div className="mt-2 space-y-2">
              {view.securitySearchResults.map((security) => <button key={security.securityId} type="button" className="w-full rounded border border-border/70 bg-secondary/30 px-3 py-2 text-left hover:border-primary/50 hover:bg-primary/10" onClick={() => view.selectSecurity(selectedLine.lineId, security)}><span className="block font-semibold text-foreground">{security.displayName}</span><span className="mt-1 block font-mono text-[11px] text-muted-foreground">{security.securityId} / {security.classification.assetClass} / {security.classification.primaryIdentifierValue}</span></button>)}
            </div>
            {selectedLine.securityId ? <Button className="mt-2" size="sm" variant="ghost" onClick={() => view.clearSecurity(selectedLine.lineId)}><X className="h-3.5 w-3.5" aria-hidden="true" />Clear security</Button> : null}
          </div>
        </div>
      ) : <p className="mt-3 text-sm text-muted-foreground">Select a line to inspect attribution.</p>}
    </div>
  );
}

function DimensionField({ label, value, placeholder, onChange }: { label: string; value: string; placeholder: string; onChange: (value: string | null) => void }) {
  return (
    <label className="space-y-1">
      <span className="text-xs font-semibold uppercase text-muted-foreground">{label}</span>
      <input className="min-h-9 w-full rounded border border-border bg-background px-2 font-mono" value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value || null)} />
    </label>
  );
}
