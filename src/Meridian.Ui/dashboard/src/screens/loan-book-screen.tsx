/**
 * Direct-lending loan book for Portfolio → Loan book.
 *
 * `/api/loans/portfolio` has been served all along and no browser module called it, so
 * the loan book was reachable only from the desktop client. This is the read surface:
 * portfolio totals, a status census, and one row per facility. Loan servicing commands
 * — drawdowns, accruals, collateral, servicer statements — stay desktop-owned and are
 * deliberately absent rather than stubbed.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { Landmark, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import { getLoanPortfolioSummary } from "@/lib/api/direct-lending.api";
import {
  buildLoanBookViewModel,
  type LoanBookRowViewModel,
  type LoanBookTone
} from "@/screens/loan-book-screen.view-model";
import type { LoanPortfolioSummary } from "@/types/direct-lending.types";

const toneClassName: Record<LoanBookTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const badgeVariant: Record<LoanBookTone, "default" | "success" | "warning" | "danger"> = {
  default: "default",
  success: "success",
  warning: "warning",
  danger: "danger"
};

const loanColumns: DenseDataTableColumn<LoanBookRowViewModel>[] = [
  {
    id: "facility",
    label: "Facility",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.facilityName}</span>
        <span className="block text-xs text-muted-foreground">{row.borrowerName}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <Badge variant={badgeVariant[row.statusTone]} dot>
        {row.statusLabel}
      </Badge>
    )
  },
  {
    id: "commitment",
    label: "Commitment",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.commitmentLabel}</span>
  },
  {
    id: "principal",
    label: "Principal outstanding",
    align: "right",
    render: (row) => (
      <span className="font-mono tabular-nums text-foreground">{row.principalOutstandingLabel}</span>
    )
  },
  {
    id: "interest",
    label: "Interest accrued",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.interestAccruedLabel}</span>
  },
  {
    id: "available",
    label: "Available to draw",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.availableToDrawLabel}</span>
  },
  {
    id: "maturity",
    label: "Maturity",
    render: (row) => <span className="font-mono text-muted-foreground">{row.maturityLabel}</span>
  },
  {
    id: "last-payment",
    label: "Last payment",
    render: (row) => <span className="font-mono text-muted-foreground">{row.lastPaymentLabel}</span>
  }
];

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "Request failed.";
}

export function LoanBookScreen({ summary }: { summary?: LoanPortfolioSummary | null } = {}) {
  const [loaded, setLoaded] = useState<LoanPortfolioSummary | null>(null);
  const [loading, setLoading] = useState(summary === undefined);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setLoaded(await getLoanPortfolioSummary());
    } catch (reason) {
      // A failed read is not an empty loan book. Keep them distinguishable: clear the
      // stale summary and say what went wrong rather than rendering a zeroed portfolio.
      setLoaded(null);
      setError(errorMessage(reason));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (summary !== undefined) {
      return;
    }

    void refresh();
  }, [refresh, summary]);

  const resolved = summary !== undefined ? summary : loaded;
  const view = useMemo(() => buildLoanBookViewModel(resolved), [resolved]);

  return (
    <Card className="panel-surface">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            <Landmark className="h-4 w-4 text-primary" aria-hidden="true" />
            {view.title}
          </CardTitle>
          <CardDescription>{view.description}</CardDescription>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void refresh()}
          disabled={loading}
          disabledReason={loading ? "A loan book read is already in flight." : undefined}
          aria-label="Refresh the loan book"
          className="shrink-0"
        >
          <RefreshCcw className="mr-2 h-3.5 w-3.5" aria-hidden="true" />
          {loading ? "Refreshing" : "Refresh"}
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">
          {view.statusAnnouncement}
        </span>

        {error ? (
          <StatusBanner
            tone="danger"
            role="alert"
            title="Loan book could not be loaded"
            detail={`${error} Retry, or confirm the direct-lending service is reachable.`}
          />
        ) : null}

        {loading && !view.loaded ? (
          <p role="status" className="text-sm text-muted-foreground">
            Loading the direct-lending loan book.
          </p>
        ) : null}

        {view.loaded ? (
          <>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-8">
              {view.metrics.map((metric) => (
                <div
                  key={metric.id}
                  role="group"
                  aria-label={metric.ariaLabel}
                  className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2 text-center"
                >
                  <div className="text-xs text-muted-foreground">{metric.label}</div>
                  <div className={cn("mt-1 font-mono text-lg font-semibold tabular-nums", toneClassName[metric.tone])}>
                    {metric.value}
                  </div>
                </div>
              ))}
            </div>

            <DenseDataTable
              columns={loanColumns}
              rows={view.rows}
              getRowId={(row) => row.loanId}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={view.emptyText}
              ariaLabel={view.tableAriaLabel}
            />
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}
