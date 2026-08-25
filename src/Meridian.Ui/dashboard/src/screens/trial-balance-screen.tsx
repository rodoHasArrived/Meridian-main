import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import { Link } from "react-router-dom";
import { AccountTree, type AccountNode } from "@/components/accounting/AccountTree";
import { AccountingTrialBalanceSelectedDetailPanel, trialBalanceColumns } from "@/components/accounting/TrialBalanceRowDetail";
import { TrialBalanceTable } from "@/components/accounting/TrialBalanceTable";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import { usePostedLedgerRouteScope } from "@/screens/posted-ledger-route-scope";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { ScreenLayout, type FocusSignal } from "@/components/ui/screen-layout";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { DENSE_VIRTUALIZATION_THRESHOLD } from "@/lib/dense-table-virtualization";
import { buildAccountingLedgerJournalEvidenceViewState } from "@/screens/accounting-screen.view-model";
import {
  collectPostedLedgerRelatedSecurities,
  useAccountingPostedLedgerViewModel
} from "@/screens/accounting-screen.posted-ledger.view-model";
import { buildTrialBalanceAccountTreeNodes, trialBalanceAccountTreeCode } from "@/screens/trial-balance-screen.view-model";
import type { AccountingLedgerJournalEvidenceViewState } from "@/screens/accounting-screen.view-model";

type TrialBalanceViewMode = "table" | "hierarchy";

/**
 * Reads only the governed posted journal. It takes no aggregate accounting-workspace payload:
 * every value on screen comes from this screen's own /api/ledger/* requests, and accepting one
 * previously meant an unrelated request's failure blanked a book that had loaded fine.
 */
export function TrialBalanceScreen() {
  const [viewMode, setViewMode] = useState<TrialBalanceViewMode>("table");
  const ledgerBook = "Primary GL";

  // The trial balance is the fund's book of record, so it reads the posted journal by
  // ledger period. It used to read the selected strategy run's simulation ledger, which
  // meant this screen — the one an operator reaches for "Accounting → Trial Balance" —
  // showed numbers that were never the fund's.
  // This screen renders the posted journal, so it is the consumer that asks for it.
  const postedLedger = useAccountingPostedLedgerViewModel("ledger", undefined, { includeJournal: true });
  const { journalLines, journalLoading, journalErrorText, selectedPeriodId, selectedPeriodLabel } = postedLedger;
  const periodOptions = postedLedger.view.periodSelector.options;

  // The book and period this surface shows travel in the URL, through the one binding both
  // ledger tabs share.
  usePostedLedgerRouteScope(postedLedger);

  const journalEvidence: AccountingLedgerJournalEvidenceViewState = useMemo(
    () => buildAccountingLedgerJournalEvidenceViewState({
      runId: selectedPeriodId,
      rows: journalLines,
      // These entries are posted to a fund period, not produced by a strategy run. Without this
      // the shared builder calls them "the selected ledger run" — the exact conflation this
      // screen was repointed to end.
      scopeLabel: selectedPeriodLabel
        ? `the posted journal for period ${selectedPeriodLabel}`
        : "the posted journal",
      // The trial balance on this same page is labelled in the book's base currency; without this
      // its journal evidence labelled the same governed debits and credits in dollars.
      currency: postedLedger.view.baseCurrency
    }),
    [journalLines, postedLedger.view.baseCurrency, selectedPeriodId, selectedPeriodLabel]
  );

  const treeNodes: AccountNode[] = useMemo(
    () => buildTrialBalanceAccountTreeNodes(postedLedger.view.trialBalance.rows),
    [postedLedger.view.trialBalance.rows]
  );

  const selectedTreeCode = useMemo(() => {
    const selectedRow = postedLedger.view.trialBalance.rows.find(
      (row) => row.rowId === postedLedger.view.trialBalance.selectedRowId
    );
    return selectedRow ? trialBalanceAccountTreeCode(selectedRow) : undefined;
  }, [postedLedger.view.trialBalance.rows, postedLedger.view.trialBalance.selectedRowId]);

  const shouldVirtualizeTrialBalance =
    postedLedger.view.trialBalance.rows.length > DENSE_VIRTUALIZATION_THRESHOLD;

  const relatedSecurities = useMemo(
    () => collectPostedLedgerRelatedSecurities(postedLedger.view.trialBalance.rows),
    [postedLedger.view.trialBalance.rows]
  );

  // Hoisted above the early returns below. A book with no periods still has to offer the book
  // picker: without it an operator whose default book is empty or failing has no way to reach
  // another book they can read, and the screen is a dead end rather than an empty state.
  const bookSelector = postedLedger.view.bookOptions.length > 1 ? (
    <FormRow label="Ledger book" labelFor="trial-balance-book-select" className="w-full max-w-xs sm:w-56">
      <Select
        id="trial-balance-book-select"
        value={postedLedger.view.bookOptions.find((option) => option.isSelected)?.id ?? ""}
        onChange={(event) => postedLedger.selectBook(event.target.value)}
      >
        {postedLedger.view.bookOptions.map((option) => (
          <option key={option.id} value={option.id}>
            {option.label} · {option.baseCurrency}
          </option>
        ))}
      </Select>
    </FormRow>
  ) : null;

  // Deliberately not gated on the aggregate accounting-workspace payload any more: every value
  // this screen renders now comes from the posted-ledger hook's own /api/ledger/* requests. Gating
  // on `data` meant a partial outage of the unrelated workspace request hid a perfectly good
  // posted book indefinitely.
  if (postedLedger.view.periodSelector.loading && periodOptions.length === 0) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="trial-balance-loading-title"
      >
        <CardHeader>
          <CardTitle id="trial-balance-loading-title">Loading Trial Balance</CardTitle>
          <CardDescription>Reading the fund's ledger periods.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (periodOptions.length === 0 && !postedLedger.view.periodSelector.loading) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Trial Balance</CardTitle>
          <CardDescription>
            {postedLedger.view.periodSelector.errorText
              ?? postedLedger.view.periodSelector.emptyText
              ?? "No ledger periods are available for this accounting scope."}
          </CardDescription>
        </CardHeader>
        {bookSelector ? <CardContent>{bookSelector}</CardContent> : null}
      </Card>
    );
  }

  const selectedBasisLabel =
    postedLedger.view.trialBalance.basisOptions.find((option) => option.isSelected)?.label ?? "—";
  // From the whole selected basis, not the filtered rows on screen: whether the book ties is a
  // property of the book. Summing what an account search left visible reported the book out of
  // balance by the value of everything filtered out.
  const trialBalanceVariance = postedLedger.view.trialBalance.basisVariance;
  const isTrialBalanceOutOfBalance = postedLedger.view.trialBalance.isBasisOutOfBalance;

  // Focus zone — ≤4 signals of "what needs my attention on this period right now".
  const focusSignals: FocusSignal[] = [
    {
      id: "period",
      label: "Selected period",
      value: selectedPeriodLabel ?? "—"
    },
    { id: "basis", label: "Accounting basis", value: selectedBasisLabel },
    { id: "accounts", label: "Accounts in view", value: postedLedger.view.trialBalance.filteredRowCountLabel },
    {
      id: "journal",
      label: "Journal entries",
      value: journalLoading ? "…" : String(journalEvidence.rows.length)
    }
  ];

  // A production month's posted journal can carry a very large number of entries, and the
  // route returns them all. Bound what is rendered so the panel cannot lock the tab, and say
  // plainly how many are not shown rather than presenting a truncated list as the whole book.
  const journalRenderLimit = 250;
  const visibleJournalRows = journalEvidence.rows.slice(0, journalRenderLimit);
  const hiddenJournalRowCount = journalEvidence.rows.length - visibleJournalRows.length;

  // Posted balances are in the book's own base currency. The simulation screen this was adapted
  // from assumed USD; a EUR book's imbalance must not be labelled with a dollar sign.
  const postedCurrency = postedLedger.view.baseCurrency;
  const formatPostedAmount = (value: number) =>
    postedCurrency
      ? new Intl.NumberFormat("en-US", { style: "currency", currency: postedCurrency }).format(value)
      : new Intl.NumberFormat("en-US", { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value);

  // The selected book names the fund-structure node it belongs to. Hard-coding "All entities"
  // presented an entity-scoped governed balance as an all-entity one.
  const entityScope = postedLedger.view.bookScopeLabel ?? "All entities";
  const trialBalanceScope = `${entityScope} · ${postedLedger.view.selectedBookLabel ?? ledgerBook} · ${selectedPeriodLabel ?? "No period selected"}`;

  // Retained on the closed-period summary. Claiming none was kept asserted an evidence gap that
  // is not there, and left freshness permanently "needs review" on a perfectly good period.
  const periodCompletedAt = postedLedger.view.periodCompletedAt;
  const freshness = postedLedger.view.trialBalance.state === "loading"
    ? { value: "Loading", detail: undefined, tone: "review" as const }
    : periodCompletedAt
      ? { value: formatDateTimeLabel(periodCompletedAt), detail: "Closed-period summary completion retained with the posted journal.", tone: "ready" as const }
      : { value: "Needs review", detail: "No trial-balance as-of timestamp was retained.", tone: "review" as const };

  return (
    <ScreenLayout
      title="Trial Balance"
      scope={trialBalanceScope}
      description="Account balances from the fund's posted journal, scoped by ledger period."
      actions={
        <>
        {bookSelector}
        <FormRow label="Period" labelFor="trial-balance-period-select" className="w-full max-w-xs sm:w-64">
          <Select
            id="trial-balance-period-select"
            value={selectedPeriodId ?? ""}
            onChange={(event) => postedLedger.selectPeriod(event.target.value)}
          >
            {periodOptions.map((option) => (
              <option key={option.id} value={option.id}>
                {option.label} · {option.statusLabel}
              </option>
            ))}
          </Select>
        </FormRow>
        </>
      }
      focus={focusSignals}
      context={
        postedLedger.view.trialBalance.selectedDetail ? (
          <AccountingTrialBalanceSelectedDetailPanel
            panelId={postedLedger.view.trialBalance.detailPanelId}
            detail={postedLedger.view.trialBalance.selectedDetail}
          />
        ) : null
      }
      contextOpen={Boolean(postedLedger.view.trialBalance.selectedDetail)}
      contextLabel="Trial-balance detail"
      contextScrollLabel="Scroll account detail for ledger lines and supporting documents"
      onContextClose={() => postedLedger.selectTrialBalanceRow(null)}
    >
      <OperationalTrustSummary
        source={{ value: "Posted journal", tone: postedLedger.view.trialBalance.state === "error" ? "blocked" : "ready" }}
        scope={{ value: selectedPeriodLabel ?? "No period selected", detail: selectedBasisLabel, tone: selectedPeriodId ? "ready" : "unknown" }}
        freshness={freshness}
        completeness={{ value: isTrialBalanceOutOfBalance ? `${postedLedger.view.trialBalance.filteredRowCountLabel} · out by ${formatPostedAmount(Math.abs(trialBalanceVariance))}` : postedLedger.view.trialBalance.filteredRowCountLabel, tone: postedLedger.view.trialBalance.hasRows && !isTrialBalanceOutOfBalance ? "ready" : "review" }}
        blocker={postedLedger.view.trialBalance.errorText
          ? { value: "Trial balance unavailable", detail: postedLedger.view.trialBalance.errorText, tone: "blocked" }
          : isTrialBalanceOutOfBalance
            ? { value: "Trial balance out of balance", detail: "Resolve the remaining debit and credit variance before approval or reporting.", tone: "blocked" }
            : undefined}
      />
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Trial balance scope</CardTitle>
          <CardDescription>Confirm entity, book, and period before drilling into account, ledger, journal, or evidence detail.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 lg:grid-cols-[repeat(3,minmax(0,1fr))_auto] lg:items-end">
          <FormRow label="Entity / fund / portfolio" labelFor="trial-balance-entity-scope">
            <Input
              id="trial-balance-entity-scope"
              value={entityScope}
              readOnly
              aria-readonly="true"
            />
          </FormRow>
          <FormRow label="Book" labelFor="trial-balance-book">
            <Input
              id="trial-balance-book"
              value={postedLedger.view.selectedBookLabel ?? ledgerBook}
              readOnly
              aria-readonly="true"
            />
          </FormRow>
          <FormRow label="Period" labelFor="trial-balance-period">
            <Input
              id="trial-balance-period"
              value={selectedPeriodLabel ?? "No period selected"}
              readOnly
              aria-readonly="true"
            />
          </FormRow>
          <div className="flex flex-wrap gap-2">
            <Button type="button" size="sm" variant="outline" disabled disabledReason="Prior-period comparison requires a retained comparison run.">Compare prior period</Button>
            <Button type="button" size="sm" variant="outline" disabled disabledReason="Exports are generated through Report Preview and Validation.">Export</Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.reportingPreviewValidation}>Jump to report preview</Link>
            </Button>
          </div>
          <TechnicalDetails label="System details" className="mt-3">
            <dl className="grid gap-2 text-xs sm:grid-cols-2">
              <div><dt className="text-muted-foreground">Period ID</dt><dd className="font-mono text-foreground">{selectedPeriodId ?? "Not supplied"}</dd></div>
              <div><dt className="text-muted-foreground">Selected row ID</dt><dd className="font-mono text-foreground">{postedLedger.view.trialBalance.selectedRowId ?? "No row selected"}</dd></div>
            </dl>
          </TechnicalDetails>
        </CardContent>
      </Card>

      <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle id="trial-balance-title">{postedLedger.view.trialBalance.title}</CardTitle>
            <CardDescription id="trial-balance-description">{postedLedger.view.trialBalance.description}</CardDescription>
          </div>
          <div className="flex gap-2" role="group" aria-label="Trial balance view mode">
            <Button
              type="button"
              size="sm"
              variant={viewMode === "table" ? "default" : "outline"}
              aria-pressed={viewMode === "table"}
              onClick={() => setViewMode("table")}
            >
              Table
            </Button>
            <Button
              type="button"
              size="sm"
              variant={viewMode === "hierarchy" ? "default" : "outline"}
              aria-pressed={viewMode === "hierarchy"}
              onClick={() => setViewMode("hierarchy")}
            >
              Hierarchy
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <span className="sr-only" aria-live="polite">{postedLedger.view.trialBalance.statusAnnouncement}</span>
          <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Accounting basis">
            {postedLedger.view.trialBalance.basisOptions.map((option) => (
              <Button
                key={option.id}
                type="button"
                size="sm"
                variant={option.isSelected ? "default" : "outline"}
                aria-pressed={option.isSelected}
                aria-label={`${option.label} basis, ${option.rowCountLabel}. ${option.description}`}
                onClick={() => postedLedger.selectBasis(option.id)}
              >
                <span>{option.label}</span>
                <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
              </Button>
            ))}
          </div>
          <div className="mb-4 rounded-md border border-border/70 bg-secondary/15 p-3">
            <FormRow label={postedLedger.view.trialBalance.accountFilterLabel} labelFor="trial-balance-account-filter">
              <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
                <div className="relative min-w-0 flex-1">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
                  <Input
                    id="trial-balance-account-filter"
                    type="search"
                    value={postedLedger.view.trialBalance.accountFilterValue}
                    onChange={(event) => postedLedger.updateAccountFilter(event.target.value)}
                    placeholder={postedLedger.view.trialBalance.accountFilterPlaceholder}
                    className="pl-9"
                  />
                </div>
                <span className="font-mono text-xs text-muted-foreground">{postedLedger.view.trialBalance.filteredRowCountLabel}</span>
              </div>
            </FormRow>
          </div>
          {postedLedger.view.trialBalance.hasRows ? (
            viewMode === "table" ? (
              shouldVirtualizeTrialBalance ? (
                <DenseDataTable
                  columns={trialBalanceColumns}
                  rows={postedLedger.view.trialBalance.rows}
                  getRowId={(line) => line.rowId}
                  getRowAriaLabel={(line) => line.ariaLabel}
                  getRowSelectAriaLabel={(line) => line.selectAriaLabel}
                  getRowAriaControls={(line) => line.detailPanelId}
                  getRowAriaExpanded={(line) => line.isExpanded}
                  getRowTypeaheadText={(line) => line.accountLabel}
                  selectedRowId={postedLedger.view.trialBalance.selectedRowId}
                  onRowSelect={(line) => postedLedger.selectTrialBalanceRow(line.rowId)}
                  emptyText={postedLedger.view.trialBalance.emptyDetail}
                  ariaLabel={postedLedger.view.trialBalance.tableLabel}
                  virtualization={{ rowHeight: 36, viewportRowCount: 15 }}
                />
              ) : (
                <TrialBalanceTable
                  rows={postedLedger.view.trialBalance.rows}
                  selectedRowId={postedLedger.view.trialBalance.selectedRowId}
                  caption={postedLedger.view.trialBalance.tableLabel}
                  // Posted balances are in the book's own units. The table defaults to USD, so
                  // without this a EUR or GBP book's debits and credits were labelled as dollars.
                  currency={postedCurrency ?? ""}
                  onRowSelect={(line) => postedLedger.selectTrialBalanceRow(line.rowId)}
                />
              )
            ) : (
              <AccountTree
                nodes={treeNodes}
                selectedCode={selectedTreeCode}
                onSelect={(node) => {
                  const row = postedLedger.view.trialBalance.rows.find(
                    (candidate) => trialBalanceAccountTreeCode(candidate) === node.code
                  );
                  if (row) {
                    postedLedger.selectTrialBalanceRow(row.rowId);
                  }
                }}
              />
            )
          ) : (
            <div
              role={postedLedger.view.trialBalance.state === "error" ? "alert" : "status"}
              className={cn(
                "rounded-lg border px-4 py-4",
                postedLedger.view.trialBalance.state === "error"
                  ? "border-danger/35 bg-danger/10 text-danger"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
              )}
            >
              <div className="text-sm font-semibold text-foreground">{postedLedger.view.trialBalance.emptyTitle}</div>
              <p className="mt-2 text-sm leading-6">
                {postedLedger.view.trialBalance.errorText ?? postedLedger.view.trialBalance.loadingText ?? postedLedger.view.trialBalance.emptyDetail}
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>{journalEvidence.title}</CardTitle>
          <CardDescription>{journalEvidence.description}</CardDescription>
        </CardHeader>
        <CardContent>
          {journalLoading ? (
            <p className="text-sm text-muted-foreground">Loading posted journal entries for this period.</p>
          ) : journalErrorText ? (
            <StatusBanner role="alert" tone="warning" title="Journal lineage unavailable" detail={journalErrorText} />
          ) : journalEvidence.hasRows ? (
            <ul className="space-y-2" aria-label={journalEvidence.title}>
              {visibleJournalRows.map((row) => (
                <li key={row.journalEntryId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <Link
                      className="min-w-0 truncate text-sm font-semibold text-primary underline-offset-2 hover:underline"
                      to={workstationRouteWithQuery("accountingJournalEntryDetail", {
                        journalEntryId: row.journalEntryId,
                        periodId: selectedPeriodId
                      })}
                    >
                      {row.description || row.journalEntryId}
                    </Link>
                    <Badge variant="outline">{row.amountLabel}</Badge>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">{row.timestampLabel} - {row.lineCountLabel}</p>
                </li>
              ))}
              {hiddenJournalRowCount > 0 ? (
                <li className="rounded-md border border-dashed border-border/70 px-3 py-2 text-xs text-muted-foreground">
                  Showing the first {journalRenderLimit} of {journalEvidence.rows.length} posted entries. Open the
                  journal-entry surfaces for the full period.
                </li>
              ) : null}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground">{journalEvidence.emptyText}</p>
          )}
        </CardContent>
      </Card>

      {relatedSecurities.length > 0 ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Securities in this run</CardTitle>
            <CardDescription>Jump to Asset Detail for a security referenced by this trial balance.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {relatedSecurities.map((security) => (
                <Button asChild key={security.securityId} size="sm" variant="outline">
                  <Link
                    to={workstationRouteWithQuery("accountingAssetDetail", { securityId: security.securityId })}
                  >
                    {security.label}
                  </Link>
                </Button>
              ))}
            </div>
          </CardContent>
        </Card>
      ) : null}

      <p className="text-xs text-muted-foreground">
        Looking for the full ledger explorer? <Link className="text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.accountingLedger}>Open Ledger Explorer</Link>.
      </p>
    </ScreenLayout>
  );
}
