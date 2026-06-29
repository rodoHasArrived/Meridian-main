import { Search } from "lucide-react";
import { Link } from "react-router-dom";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { cn } from "@/lib/utils";
import type {
  AccountingReportingViewState,
  AccountingTrialBalanceDetailViewState,
  AccountingTrialBalanceRowViewModel,
  AccountingTrialBalanceViewState,
  ReconciliationDetailActionsViewModel
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSavedViewSaveRequestDto
} from "@/types";

const trialBalanceColumns: DenseDataTableColumn<AccountingTrialBalanceRowViewModel>[] = [
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
          row.balanceTone === "danger" ? "text-danger" : row.balanceTone === "success" ? "text-success" : "text-foreground"
        )}
      >
        {row.balanceLabel}
      </span>
    )
  },
  { id: "policy", label: "Policy", render: (row) => <span className="text-muted-foreground">{row.policyLabel}</span> },
  { id: "entries", label: "Entries", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.entryCountLabel}</span> }
];

export interface AccountingLedgerTransactionLabViewState {
  title: string;
  description: string;
  statusTone: "default" | "success" | "warning" | "danger";
  requestSummaryLabel: string;
  statusRole: "status" | "alert";
  statusText: string;
  journalLineCountLabel: string;
  ledgerImpactLabel: string;
  reconciliationLabel: string;
  evidenceLabel: string;
  impactRows: Array<{ id: string; label: string; value: string; tone: "default" | "success" | "warning" | "danger" }>;
  canPreview: boolean;
  disabledReason: string | null;
  busy: boolean;
  previewButtonLabel: string;
  previewButtonAriaLabel: string;
}

export interface AccountingLedgerReportingControls extends AccountingReportingViewState {
  runExport: () => void | Promise<void>;
}

export interface AccountingLedgerExplorerPanelProps {
  selectedReconciliation: AccountingWorkspaceResponse["reconciliationQueue"][number];
  selectedOpenBreakLabel: string;
  selectedOpenBreakTone: "success" | "warning";
  trialBalanceView: AccountingTrialBalanceViewState;
  transactionLabView: AccountingLedgerTransactionLabViewState;
  reporting: AccountingLedgerReportingControls;
  explorer: FinancialRecordExplorerDto | null;
  detailActions: ReconciliationDetailActionsViewModel | null;
  onSaveView: (request: FinancialRecordExplorerSavedViewSaveRequestDto) => void | Promise<void>;
  onSelectAccountingBasis: (basis: AccountingTrialBalanceViewState["selectedBasis"]) => void;
  onUpdateLedgerAccountFilter: (value: string) => void;
  onSelectTrialBalanceRow: (rowId: string) => void;
  onRunTransactionLabPreview: () => void | Promise<void>;
}

export function AccountingLedgerExplorerPanel({
  selectedReconciliation,
  selectedOpenBreakLabel,
  selectedOpenBreakTone,
  trialBalanceView,
  transactionLabView,
  reporting,
  explorer,
  detailActions,
  onSaveView,
  onSelectAccountingBasis,
  onUpdateLedgerAccountFilter,
  onSelectTrialBalanceRow,
  onRunTransactionLabPreview
}: AccountingLedgerExplorerPanelProps) {
  const selectedBasisLabel = trialBalanceView.basisOptions.find((option) => option.isSelected)?.label ?? "Primary";

  return (
    <FinancialRecordExplorerShell
      explorerLabel="Financial Record Explorer"
      title="Ledger Explorer"
      titleId="accounting-ledger-explorer-title"
      description="Filter accounting ledger records, inspect dense trial-balance rows, and drill into journals, ledger lines, source documents, approvals, reconciliations, report usage, and audit history without leaving the Accounting workspace."
      scopeItems={[
        { id: "workspace", label: "Workspace", value: "Accounting" },
        { id: "record-set", label: "Record set", value: "Journal entries and ledger detail" },
        { id: "run", label: "Reconciliation run", value: selectedReconciliation.strategyName },
        { id: "run-id", label: "Run ID", value: selectedReconciliation.runId }
      ]}
      savedViews={[
        {
          id: "controller-review",
          label: "Controller review",
          detail: "Default ledger explorer view for trial balance, proof drawer, approvals, and report usage.",
          active: true
        },
        {
          id: "exceptions",
          label: "Exceptions",
          detail: "Focuses the ledger grid on unreconciled accounts, blockers, and missing evidence."
        },
        {
          id: "report-usage",
          label: "Report usage",
          detail: "Keeps journal, ledger line, and report export proof paths visible together."
        }
      ]}
      summaryItems={[
        { id: "rows", label: "Rows", value: trialBalanceView.filteredRowCountLabel },
        { id: "basis", label: "Basis", value: selectedBasisLabel },
        { id: "breaks", label: "Open breaks", value: selectedOpenBreakLabel, tone: selectedOpenBreakTone },
        { id: "reconciliation", label: "Reconciliation", value: selectedReconciliation.reconciliationStatus, tone: selectedOpenBreakTone }
      ]}
      appliedFilters={[
        { id: "account", label: "GL account", value: trialBalanceView.accountFilterValue.trim() || "All accounts" },
        { id: "basis-filter", label: "Accounting basis", value: selectedBasisLabel },
        { id: "run-filter", label: "Run", value: selectedReconciliation.runId }
      ]}
      actions={[
        {
          id: "evidence",
          label: detailActions?.evidencePacketLabel ?? "Open evidence packet",
          href: detailActions?.evidencePacketHref,
          ariaLabel: detailActions?.evidencePacketAriaLabel
        },
        {
          id: "audit",
          label: detailActions?.auditPacketLabel ?? "Review audit packet",
          href: detailActions?.auditPacketHref,
          ariaLabel: detailActions?.auditPacketAriaLabel
        }
      ]}
      explorer={explorer}
      onSaveView={onSaveView}
    >
      <div className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <TrialBalancePanel
          view={trialBalanceView}
          onSelectAccountingBasis={onSelectAccountingBasis}
          onUpdateLedgerAccountFilter={onUpdateLedgerAccountFilter}
          onSelectTrialBalanceRow={onSelectTrialBalanceRow}
        />
        <LedgerSupportPanel
          trialBalanceView={trialBalanceView}
          transactionLabView={transactionLabView}
          reporting={reporting}
          onRunTransactionLabPreview={onRunTransactionLabPreview}
        />
      </div>
    </FinancialRecordExplorerShell>
  );
}

function TrialBalancePanel({
  view,
  onSelectAccountingBasis,
  onUpdateLedgerAccountFilter,
  onSelectTrialBalanceRow
}: {
  view: AccountingTrialBalanceViewState;
  onSelectAccountingBasis: (basis: AccountingTrialBalanceViewState["selectedBasis"]) => void;
  onUpdateLedgerAccountFilter: (value: string) => void;
  onSelectTrialBalanceRow: (rowId: string) => void;
}) {
  return (
    <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
      <CardHeader>
        <CardTitle id="trial-balance-title">{view.title}</CardTitle>
        <CardDescription id="trial-balance-description">{view.description}</CardDescription>
      </CardHeader>
      <CardContent>
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Accounting basis">
          {view.basisOptions.map((option) => (
            <Button
              key={option.id}
              type="button"
              size="sm"
              variant={option.isSelected ? "default" : "outline"}
              aria-pressed={option.isSelected}
              aria-label={`${option.label} basis, ${option.rowCountLabel}. ${option.description}`}
              onClick={() => onSelectAccountingBasis(option.id)}
            >
              <span>{option.label}</span>
              <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
            </Button>
          ))}
        </div>
        <LedgerAccountFilter view={view} onUpdateLedgerAccountFilter={onUpdateLedgerAccountFilter} />
        {view.hasRows ? (
          <div className="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
            <DenseDataTable
              columns={trialBalanceColumns}
              rows={view.rows}
              getRowId={(line) => line.rowId}
              getRowAriaLabel={(line) => line.ariaLabel}
              getRowSelectAriaLabel={(line) => line.selectAriaLabel}
              getRowAriaControls={(line) => line.detailPanelId}
              getRowAriaExpanded={(line) => line.isExpanded}
              selectedRowId={view.selectedRowId}
              onRowSelect={(line) => onSelectTrialBalanceRow(line.rowId)}
              emptyText={view.emptyDetail}
              ariaLabel={view.tableLabel}
            />
            {view.selectedDetail ? (
              <AccountingTrialBalanceSelectedDetailPanel
                panelId={view.detailPanelId}
                detail={view.selectedDetail}
              />
            ) : (
              <aside
                id={view.detailPanelId}
                role="region"
                aria-label={view.detailEmptyAriaLabel}
                data-selected-source="Selected from trial balance"
                className="row-detail-panel h-fit min-w-0"
              >
                <div className="eyebrow-label">Trial-balance detail</div>
                <h3 className="mt-1 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
              </aside>
            )}
          </div>
        ) : (
          <div
            role={view.state === "error" ? "alert" : "status"}
            className={cn(
              "rounded-lg border px-4 py-4",
              view.state === "error"
                ? "border-danger/35 bg-danger/10 text-danger"
                : "border-border/70 bg-secondary/25 text-muted-foreground"
            )}
          >
            <div className="text-sm font-semibold text-foreground">{view.emptyTitle}</div>
            <p className="mt-2 text-sm leading-6">
              {view.errorText ?? view.loadingText ?? view.emptyDetail}
            </p>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {view.loadingText && view.hasRows ? (
          <p role="status" className="mt-3 text-sm text-muted-foreground">
            {view.loadingText}
          </p>
        ) : null}
        {view.errorText && view.hasRows ? (
          <StatusBanner
            role="alert"
            className="mt-3"
            tone="danger"
            title={view.errorText}
            detail={view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

function LedgerAccountFilter({
  view,
  onUpdateLedgerAccountFilter
}: {
  view: AccountingTrialBalanceViewState;
  onUpdateLedgerAccountFilter: (value: string) => void;
}) {
  return (
    <div className="mb-4 rounded-md border border-border/70 bg-secondary/15 p-3">
      <FormRow
        label={view.accountFilterLabel}
        labelFor="ledger-account-filter"
      >
        <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
          <div className="relative min-w-0 flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              id="ledger-account-filter"
              type="search"
              value={view.accountFilterValue}
              onChange={(event) => onUpdateLedgerAccountFilter(event.target.value)}
              placeholder={view.accountFilterPlaceholder}
              className="pl-9"
            />
          </div>
          <div className="flex items-center gap-2">
            <span className="font-mono text-xs text-muted-foreground">{view.filteredRowCountLabel}</span>
            {view.accountFilterValue.trim() ? (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => onUpdateLedgerAccountFilter("")}
              >
                {view.clearAccountFilterLabel}
              </Button>
            ) : null}
          </div>
        </div>
      </FormRow>
      {view.accountFilterOptions.length > 0 ? (
        <div className="mt-3 flex flex-wrap gap-2" role="group" aria-label="General Ledger account shortcuts">
          {view.accountFilterOptions.map((option) => (
            <Button
              key={option.id}
              type="button"
              size="sm"
              variant={option.isSelected ? "secondary" : "outline"}
              aria-pressed={option.isSelected}
              aria-label={`${option.label}, ${option.detail}, ${option.rowCountLabel}`}
              onClick={() => onUpdateLedgerAccountFilter(option.label)}
            >
              <span className="truncate">{option.label}</span>
              <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
            </Button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function LedgerSupportPanel({
  trialBalanceView,
  transactionLabView,
  reporting,
  onRunTransactionLabPreview
}: {
  trialBalanceView: AccountingTrialBalanceViewState;
  transactionLabView: AccountingLedgerTransactionLabViewState;
  reporting: AccountingLedgerReportingControls;
  onRunTransactionLabPreview: () => void | Promise<void>;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>{trialBalanceView.basisBridge.title}</CardTitle>
        <CardDescription>{trialBalanceView.basisBridge.description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <BasisBridgePanel view={trialBalanceView.basisBridge} />
        <TransactionLabPanel view={transactionLabView} onRunTransactionLabPreview={onRunTransactionLabPreview} />
        <ReportingExportPanel reporting={reporting} />
      </CardContent>
    </Card>
  );
}

function BasisBridgePanel({ view }: { view: AccountingTrialBalanceViewState["basisBridge"] }) {
  return (
    <div role="region" aria-label={view.tableLabel}>
      {view.hasRows ? (
        <div className="space-y-2">
          {view.rows.map((row) => (
            <div key={row.rowId} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" aria-label={row.ariaLabel}>
              <div className="flex items-start justify-between gap-3">
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold text-foreground">{row.accountLabel}</span>
                  <span className="mt-1 block text-xs text-muted-foreground">{row.sourceLabel}</span>
                </span>
                <Badge variant={row.varianceTone}>{row.varianceLabel}</Badge>
              </div>
              <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
                <span className="font-mono">Primary {row.primaryBalanceLabel}</span>
                <span className="font-mono">{row.comparisonBalanceLabel}</span>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm leading-6 text-muted-foreground">
          {view.emptyText}
        </p>
      )}
    </div>
  );
}

function TransactionLabPanel({
  view,
  onRunTransactionLabPreview
}: {
  view: AccountingLedgerTransactionLabViewState;
  onRunTransactionLabPreview: () => void | Promise<void>;
}) {
  return (
    <div className="border-t border-border/70 pt-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-foreground">{view.title}</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">{view.description}</p>
        </div>
        <Badge variant={view.statusTone === "default" ? "outline" : view.statusTone} dot>
          {view.requestSummaryLabel}
        </Badge>
      </div>
      <p
        role={view.statusRole}
        className={cn(
          "mt-3 rounded-md border px-3 py-2 text-sm",
          view.statusTone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : "",
          view.statusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
          view.statusTone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
          view.statusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : ""
        )}
      >
        {view.statusText}
      </p>
      <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
        <div className="rounded-md border border-border/70 bg-background px-3 py-2">
          <span className="block text-muted-foreground">Journal</span>
          <span className="mt-1 block font-mono text-foreground">{view.journalLineCountLabel}</span>
        </div>
        <div className="rounded-md border border-border/70 bg-background px-3 py-2">
          <span className="block text-muted-foreground">Ledger impact</span>
          <span className="mt-1 block font-mono text-foreground">{view.ledgerImpactLabel}</span>
        </div>
        <div className="rounded-md border border-border/70 bg-background px-3 py-2">
          <span className="block text-muted-foreground">Reconciliation</span>
          <span className="mt-1 block font-mono text-foreground">{view.reconciliationLabel}</span>
        </div>
        <div className="rounded-md border border-border/70 bg-background px-3 py-2">
          <span className="block text-muted-foreground">Evidence</span>
          <span className="mt-1 block font-mono text-foreground">{view.evidenceLabel}</span>
        </div>
      </div>
      {view.impactRows.length > 0 ? (
        <div className="mt-3 space-y-2" aria-label="Transaction Lab trial-balance impact">
          {view.impactRows.map((row) => (
            <div key={row.id} className="flex items-center justify-between gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
              <span className="min-w-0 truncate text-foreground">{row.label}</span>
              <Badge variant={row.tone === "default" ? "outline" : row.tone}>{row.value}</Badge>
            </div>
          ))}
        </div>
      ) : null}
      <Button
        type="button"
        variant="outline"
        className="mt-3 w-full"
        disabled={!view.canPreview}
        disabledReason={view.disabledReason}
        busy={view.busy}
        busyLabel={view.previewButtonLabel}
        aria-label={view.previewButtonAriaLabel}
        onClick={() => void onRunTransactionLabPreview()}
      >
        {view.previewButtonLabel}
      </Button>
    </div>
  );
}

function ReportingExportPanel({ reporting }: { reporting: AccountingLedgerReportingControls }) {
  return (
    <>
      <div className="border-t border-border/70 pt-4">
        <h3 className="text-sm font-semibold text-foreground">Reporting exports</h3>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">Entry points for report/export handoff using existing export infrastructure.</p>
      </div>
      <Button asChild>
        <a href={reporting.backendLinks[0].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[0].ariaLabel}>
          {reporting.backendLinks[0].label}
        </a>
      </Button>
      <Button
        type="button"
        variant="outline"
        disabled={!reporting.exportCanRun}
        disabledReason={reporting.exportDisabledReason}
        busy={reporting.exportBusy}
        busyLabel={reporting.exportButtonLabel}
        aria-label={reporting.exportAriaLabel}
        onClick={() => void reporting.runExport()}
      >
        {reporting.exportButtonLabel}
      </Button>
      <Button asChild variant="outline">
        <a href={reporting.backendLinks[1].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[1].ariaLabel}>
          {reporting.backendLinks[1].label}
        </a>
      </Button>
      {reporting.exportStatusText ? (
        <p
          role={reporting.exportStatusRole}
          className={cn(
            "rounded-lg border px-3 py-2 text-sm",
            reporting.exportStatusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
            reporting.exportStatusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
            reporting.exportStatusTone === "neutral" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
          )}
        >
          {reporting.exportStatusText}
        </p>
      ) : null}
    </>
  );
}

function AccountingTrialBalanceSelectedDetailPanel({
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
