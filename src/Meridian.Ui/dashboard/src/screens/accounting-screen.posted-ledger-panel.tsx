import { Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { AccountingTrialBalanceSelectedDetailPanel, trialBalanceColumns } from "@/components/accounting/TrialBalanceRowDetail";
import { TrialBalanceTable } from "@/components/accounting";
import { cn } from "@/lib/utils";
import { DENSE_VIRTUALIZATION_THRESHOLD } from "@/lib/dense-table-virtualization";
import type { FinancialRecordExplorerSavedView } from "@/components/meridian/financial-record-explorer";
import { useAccountingPostedLedgerViewModel } from "@/screens/accounting-screen.posted-ledger.view-model";
import type { AccountingWorkstream } from "@/screens/accounting-screen.task-mode-view-model";

/**
 * Static saved views for the strategy-run ledger explorer that renders below the
 * posted-journal panel on the ledger workstream. Hosted here rather than inline in
 * accounting-screen.tsx per the file-size ratchet.
 */
export const LEDGER_EXPLORER_SAVED_VIEWS: FinancialRecordExplorerSavedView[] = [
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
];

/**
 * The Accounting workstream's primary ledger surface: trial balance and P&L read
 * from the posted journal (the governed book of record), scoped by ledger period.
 */
export function AccountingPostedLedgerSection({ workstream }: { workstream: AccountingWorkstream }) {
  const viewModel = useAccountingPostedLedgerViewModel(workstream);
  const { view } = viewModel;

  return (
    <section aria-labelledby="posted-ledger-title" className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
      <Card aria-describedby="posted-ledger-description" className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-2">
            <CardTitle id="posted-ledger-title">{view.title}</CardTitle>
            <Badge variant="outline">{view.sourceBadgeLabel}</Badge>
          </div>
          <CardDescription id="posted-ledger-description">{view.description}</CardDescription>
        </CardHeader>
        <CardContent>
          <span className="sr-only" aria-live="polite">{view.trialBalance.statusAnnouncement}</span>
          {/*
            The shared view state has carried bookOptions since the posted ledger became
            book-scoped, but this panel rendered neither the options nor the book's name. On the
            canonical Accounting ledger surface that left every book after the first unreachable
            in a multi-book deployment, and the balances on screen unattributed. Chips rather than
            a dropdown, to match the period selector directly below.
          */}
          {view.bookOptions.length > 1 ? (
            <div className="mb-4" role="group" aria-label="Ledger book">
              <div className="mb-1 text-xs font-semibold text-muted-foreground">Ledger book</div>
              <div className="flex flex-wrap gap-2">
                {view.bookOptions.map((option) => (
                  <Button
                    key={option.id}
                    type="button"
                    size="sm"
                    variant={option.isSelected ? "default" : "outline"}
                    aria-pressed={option.isSelected}
                    aria-label={`${option.label}, base currency ${option.baseCurrency}`}
                    onClick={() => viewModel.selectBook(option.id)}
                  >
                    <span>{option.label}</span>
                    <span className="ml-2 font-mono text-[10px] opacity-75">{option.baseCurrency}</span>
                  </Button>
                ))}
              </div>
            </div>
          ) : view.selectedBookLabel ? (
            <div className="mb-4 text-xs text-muted-foreground">
              Ledger book: <span className="font-semibold text-foreground">{view.selectedBookLabel}</span>
            </div>
          ) : null}
          <div className="mb-4" role="group" aria-label={view.periodSelector.label}>
            <div className="mb-1 text-xs font-semibold text-muted-foreground">{view.periodSelector.label}</div>
            {view.periodSelector.errorText ? (
              <div role="alert" className="rounded-lg border border-danger/35 bg-danger/10 px-4 py-3 text-sm text-danger">
                <div className="font-semibold">{view.periodSelector.errorText}</div>
                {view.periodSelector.errorDetails.length > 0 ? (
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                    {view.periodSelector.errorDetails.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : view.periodSelector.options.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {view.periodSelector.options.map((option) => (
                  <Button
                    key={option.id}
                    type="button"
                    size="sm"
                    variant={option.isSelected ? "default" : "outline"}
                    aria-pressed={option.isSelected}
                    aria-label={option.ariaLabel}
                    title={option.detail}
                    onClick={() => viewModel.selectPeriod(option.id)}
                  >
                    <span>{option.label}</span>
                    <span className="ml-2 font-mono text-[10px] opacity-75">{option.statusLabel}</span>
                  </Button>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm leading-6 text-muted-foreground">
                {view.periodSelector.loadingText ?? view.periodSelector.emptyText}
              </p>
            )}
          </div>
          {view.periodNotice ? (
            <p role="status" className="mb-4 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
              {view.periodNotice}
            </p>
          ) : null}
          <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Accounting basis">
            {view.trialBalance.basisOptions.map((option) => (
              <Button
                key={option.id}
                type="button"
                size="sm"
                variant={option.isSelected ? "default" : "outline"}
                aria-pressed={option.isSelected}
                aria-label={`${option.label} basis, ${option.rowCountLabel}. ${option.description}`}
                onClick={() => viewModel.selectBasis(option.id)}
              >
                <span>{option.label}</span>
                <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
              </Button>
            ))}
          </div>
          <div className="mb-4 rounded-md border border-border/70 bg-secondary/15 p-3">
            <FormRow label={view.trialBalance.accountFilterLabel} labelFor="posted-ledger-account-filter">
              <div className="relative min-w-0 flex-1">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
                <Input
                  id="posted-ledger-account-filter"
                  type="search"
                  value={view.trialBalance.accountFilterValue}
                  onChange={(event) => viewModel.updateAccountFilter(event.target.value)}
                  placeholder={view.trialBalance.accountFilterPlaceholder}
                  className="pl-9"
                />
              </div>
            </FormRow>
            <div className="mt-2 flex items-center justify-between gap-3">
              <span className="font-mono text-xs text-muted-foreground">{view.trialBalance.filteredRowCountLabel}</span>
              {view.trialBalance.accountFilterValue.trim() ? (
                <Button type="button" size="sm" variant="outline" onClick={() => viewModel.updateAccountFilter("")}>
                  {view.trialBalance.clearAccountFilterLabel}
                </Button>
              ) : null}
            </div>
          </div>
          {view.trialBalance.hasRows ? (
            <div className="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
              {view.trialBalance.rows.length > DENSE_VIRTUALIZATION_THRESHOLD ? (
                <DenseDataTable
                  columns={trialBalanceColumns}
                  rows={view.trialBalance.rows}
                  getRowId={(line) => line.rowId}
                  getRowAriaLabel={(line) => line.ariaLabel}
                  getRowSelectAriaLabel={(line) => line.selectAriaLabel}
                  getRowAriaControls={(line) => line.detailPanelId}
                  getRowAriaExpanded={(line) => line.isExpanded}
                  selectedRowId={view.trialBalance.selectedRowId}
                  onRowSelect={(line) => viewModel.selectTrialBalanceRow(line.rowId)}
                  emptyText={view.trialBalance.emptyDetail}
                  ariaLabel={view.trialBalance.tableLabel}
                />
              ) : (
                <TrialBalanceTable
                  rows={view.trialBalance.rows}
                  selectedRowId={view.trialBalance.selectedRowId}
                  caption={view.trialBalance.tableLabel}
                  onRowSelect={(line) => viewModel.selectTrialBalanceRow(line.rowId)}
                />
              )}
              {view.trialBalance.selectedDetail ? (
                <AccountingTrialBalanceSelectedDetailPanel
                  panelId={view.trialBalance.detailPanelId}
                  detail={view.trialBalance.selectedDetail}
                />
              ) : (
                <aside
                  id={view.trialBalance.detailPanelId}
                  role="region"
                  aria-label={view.trialBalance.detailEmptyAriaLabel}
                  data-selected-source="Selected from posted-journal trial balance"
                  className="row-detail-panel h-fit min-w-0"
                >
                  <div className="eyebrow-label">Trial-balance detail</div>
                  <h3 className="mt-1 text-sm font-semibold text-foreground">{view.trialBalance.detailEmptyTitle}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.trialBalance.detailEmptyText}</p>
                </aside>
              )}
            </div>
          ) : (
            <div
              role={view.trialBalance.state === "error" ? "alert" : "status"}
              className={cn(
                "rounded-lg border px-4 py-4",
                view.trialBalance.state === "error"
                  ? "border-danger/35 bg-danger/10 text-danger"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
              )}
            >
              <div className="text-sm font-semibold text-foreground">{view.trialBalance.emptyTitle}</div>
              <p className="mt-2 text-sm leading-6">
                {view.trialBalance.errorText ?? view.trialBalance.loadingText ?? view.periodNotice ?? view.trialBalance.emptyDetail}
              </p>
              {view.trialBalance.errorDetails.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                  {view.trialBalance.errorDetails.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          )}
        </CardContent>
      </Card>
      <Card aria-labelledby="posted-ledger-pnl-title" className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-2">
            <CardTitle id="posted-ledger-pnl-title">{view.pnl.title}</CardTitle>
            {view.pnl.signoffLabel ? (
              <Badge variant={view.pnl.signoffTone}>{view.pnl.signoffLabel}</Badge>
            ) : null}
          </div>
          <CardDescription>{view.pnl.description}</CardDescription>
        </CardHeader>
        <CardContent>
          {view.pnl.state === "ready" ? (
            <div className="grid grid-cols-2 gap-2 text-xs">
              {view.pnl.items.map((item) => (
                <div key={item.id} className="rounded-md border border-border/70 bg-background px-3 py-2">
                  <span className="block text-muted-foreground">{item.label}</span>
                  <span
                    className={cn(
                      "mt-1 block font-mono text-foreground",
                      item.tone === "success" ? "text-success" : "",
                      item.tone === "warning" ? "text-warning" : "",
                      item.tone === "danger" ? "text-danger" : ""
                    )}
                  >
                    {item.value}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <p
              role={view.pnl.state === "error" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-3 py-2 text-sm leading-6",
                view.pnl.state === "error"
                  ? "border-danger/35 bg-danger/10 text-danger"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
              )}
            >
              {view.pnl.errorText ?? view.pnl.emptyText}
            </p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}
