import { Link } from "react-router-dom";
import { DenseDataTableColumn, EntitySummary } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type {
  AccountingTrialBalanceRowViewModel,
  AccountingTrialBalanceDetailViewState
} from "@/screens/accounting-screen.view-model";

export const trialBalanceColumns: DenseDataTableColumn<AccountingTrialBalanceRowViewModel>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.accountLabel}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.financialAccountId ?? "Unassigned"}</span>
        <span className="mt-1 block break-words font-mono text-[11px] text-muted-foreground">{row.dimensionLabel}</span>
      </span>
    )
  },
  { id: "type", label: "Type", render: (row) => <span className="font-mono text-muted-foreground">{row.accountTypeLabel}</span> },
  { id: "basis", label: "Basis", render: (row) => <Badge variant={row.basisTone}>{row.basisLabel}</Badge> },
  {
    id: "balance",
    label: "Balance",
    align: "right",
    render: (row) => (
      <span
        className={cn(
          "font-mono tabular-nums",
          row.balanceTone === "success" ? "text-success" : row.balanceTone === "danger" ? "text-danger" : "text-foreground"
        )}
      >
        {row.balanceLabel}
      </span>
    )
  },
  { id: "entries", label: "Entries", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.entryCountLabel}</span> }
];

export function AccountingTrialBalanceSelectedDetailPanel({
  panelId,
  detail
}: {
  panelId: string;
  detail: AccountingTrialBalanceDetailViewState;
}) {
  return (
    <div id={panelId} className="min-w-0">
      <EntitySummary
        eyebrow={detail.eyebrow}
        title={detail.title}
        subtitle={detail.subtitle}
        description={detail.description}
        status={<Badge variant={detail.statusVariant} dot>{detail.statusLabel}</Badge>}
        fields={detail.fields}
        ariaLabel={detail.ariaLabel}
      />
      <div className="mt-3 flex flex-wrap gap-2" aria-label="Trial balance audit drill-through actions">
        {detail.auditDrillThroughHref ? (
          <Button asChild size="sm" variant="secondary">
            <Link to={detail.auditDrillThroughHref}>{detail.auditDrillThroughLabel}</Link>
          </Button>
        ) : (
          <span className="text-xs text-muted-foreground">{detail.auditDrillThroughLabel}</span>
        )}
        {detail.approvalDrillThroughHref ? (
          <Button asChild size="sm" variant="outline">
            <Link to={detail.approvalDrillThroughHref}>Open approval evidence</Link>
          </Button>
        ) : null}
      </div>
      <div className="mt-4 rounded-md border border-border/70 bg-background/60 p-3">
        <h3 className="text-sm font-semibold text-foreground">{detail.ledgerLinesTitle}</h3>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.ledgerLinesDescription}</p>
        {detail.ledgerLines.length > 0 ? (
          <div className="mt-3 space-y-2" role="list" aria-label={detail.ledgerLinesTitle}>
            {detail.ledgerLines.map((line) => (
              <div key={line.rowId} role="listitem" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" aria-label={line.ariaLabel}>
                <div className="flex items-start justify-between gap-3">
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold text-foreground">{line.description}</span>
                    <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{line.journalEntryId}</span>
                  </span>
                  <Badge variant="outline">{line.balanceLabel}</Badge>
                </div>
                <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
                  <span className="font-mono">Debit {line.debitLabel}</span>
                  <span className="font-mono">Credit {line.creditLabel}</span>
                </div>
                <div className="mt-2 flex flex-wrap gap-2 text-xs">
                  {line.evidenceHref ? (
                    <Link className="text-primary underline-offset-2 hover:underline" to={line.evidenceHref}>
                      {line.evidenceLabel}
                    </Link>
                  ) : (
                    <span className="text-muted-foreground">{line.evidenceLabel}</span>
                  )}
                  {line.approvalHref ? (
                    <Link className="text-primary underline-offset-2 hover:underline" to={line.approvalHref}>
                      Approval evidence
                    </Link>
                  ) : null}
                </div>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="mt-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
            {detail.ledgerLinesEmptyText}
          </p>
        )}
      </div>
      <div className="mt-4 rounded-md border border-border/70 bg-background/60 p-3">
        <h3 className="text-sm font-semibold text-foreground">{detail.supportingDocumentsTitle}</h3>
        {detail.supportingDocuments.length > 0 ? (
          <div className="mt-3 space-y-2" role="list" aria-label={detail.supportingDocumentsTitle}>
            {detail.supportingDocuments.map((document) => (
              <div key={document.id} role="listitem" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="text-sm font-semibold text-foreground">
                  {document.href ? (
                    document.href.startsWith("/accounting") ? (
                      <Link className="text-primary underline-offset-2 hover:underline" to={document.href} aria-label={document.ariaLabel}>
                        {document.label}
                      </Link>
                    ) : (
                      <a className="text-primary underline-offset-2 hover:underline" href={document.href} target="_blank" rel="noreferrer" aria-label={document.ariaLabel}>
                        {document.label}
                      </a>
                    )
                  ) : (
                    document.label
                  )}
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{document.detail}</p>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="mt-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
            {detail.supportingDocumentsEmptyText}
          </p>
        )}
      </div>
    </div>
  );
}
