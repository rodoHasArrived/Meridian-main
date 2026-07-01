import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { LedgerTable, type LedgerRow } from "@/components/accounting/LedgerTable";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { getManualJournalEntryWorkbench, getRunLedgerJournal } from "@/lib/api";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { buildJournalEntryDetailViewState } from "@/screens/journal-entry-detail-screen.view-model";
import type { LedgerJournalLine, ManualJournalEntryDraft } from "@/types";

export function JournalEntryDetailScreen() {
  const [searchParams] = useSearchParams();
  const journalEntryId = searchParams.get("journalEntryId") ?? "";
  const runId = searchParams.get("runId");

  const [draft, setDraft] = useState<ManualJournalEntryDraft | null>(null);
  const [journalLine, setJournalLine] = useState<LedgerJournalLine | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!journalEntryId) {
      setDraft(null);
      setJournalLine(null);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);

    getManualJournalEntryWorkbench({})
      .then((workbench) => {
        if (cancelled) {
          return;
        }

        const matchedDraft = workbench.drafts.find((candidate) => candidate.journalEntryId === journalEntryId) ?? null;
        setDraft(matchedDraft);

        if (matchedDraft || !runId) {
          setLoading(false);
          return;
        }

        return getRunLedgerJournal(runId)
          .then((lines) => {
            if (!cancelled) {
              setJournalLine(lines.find((line) => line.journalEntryId === journalEntryId) ?? null);
            }
          })
          .finally(() => {
            if (!cancelled) {
              setLoading(false);
            }
          });
      })
      .catch(() => {
        if (!cancelled) {
          setDraft(null);
          setJournalLine(null);
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [journalEntryId, runId]);

  if (!journalEntryId) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Journal Entry Detail</CardTitle>
          <CardDescription>Open this page from a trial balance, ledger, or reconciliation row to inspect one posting.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (loading) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="journal-entry-loading-title"
      >
        <CardHeader>
          <CardTitle id="journal-entry-loading-title">Loading journal entry</CardTitle>
          <CardDescription>Looking up {journalEntryId}.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const view = buildJournalEntryDetailViewState({ journalEntryId, draft, journalLine });

  const lineRows: LedgerRow[] = view.lines.map((line) => ({
    date: draft?.accountingDate ?? "",
    ref: view.journalEntryId,
    memo: line.description ?? "",
    account: line.account,
    debit: line.debit,
    credit: line.credit
  }));

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>{view.title}</CardTitle>
            <CardDescription>Journal entry {view.journalEntryId}</CardDescription>
          </div>
          <Badge variant={view.statusTone} dot>{view.statusLabel}</Badge>
        </CardHeader>
        <CardContent>
          {view.notFoundText ? (
            <StatusBanner role="alert" tone="warning" title="Journal entry not found" detail={view.notFoundText} />
          ) : (
            <>
              {view.summaryOnlyNotice ? (
                <StatusBanner
                  role="status"
                  tone="warning"
                  title="Summary-only entry"
                  detail={view.summaryOnlyNotice}
                  className="mb-4"
                />
              ) : null}
              <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {view.summaryFields.map((field) => (
                  <div key={field.label}>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                    <dd className="mt-1 text-sm text-foreground">{field.value}</dd>
                  </div>
                ))}
              </dl>
            </>
          )}
        </CardContent>
      </Card>

      {view.dataCompleteness === "full" ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Lines</CardTitle>
            <CardDescription>Debit and credit lines for this posting.</CardDescription>
          </CardHeader>
          <CardContent>
            <LedgerTable rows={lineRows} currency={view.currency} showAccount caption={`Lines for journal entry ${view.journalEntryId}`} />
          </CardContent>
        </Card>
      ) : null}

      {view.dataCompleteness === "full" ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Lifecycle</CardTitle>
            <CardDescription>Posting lifecycle history for this entry.</CardDescription>
          </CardHeader>
          <CardContent>
            {view.lifecycle.length > 0 ? (
              <ul className="space-y-2" aria-label="Journal entry lifecycle">
                {view.lifecycle.map((transition) => (
                  <li key={transition.transitionId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="text-sm font-semibold text-foreground">{transition.label}</span>
                      <span className="text-xs text-muted-foreground">{transition.recordedAtUtc}</span>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">By {transition.actor}{transition.notes ? ` - ${transition.notes}` : ""}</p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No lifecycle transitions recorded yet.</p>
            )}
          </CardContent>
        </Card>
      ) : null}

      {view.dataCompleteness === "full" ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Evidence</CardTitle>
            <CardDescription>Documents attached to this journal entry.</CardDescription>
          </CardHeader>
          <CardContent>
            {view.evidence.length > 0 ? (
              <ul className="space-y-2" aria-label="Journal entry evidence">
                {view.evidence.map((attachment) => (
                  <li key={attachment.attachmentId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                    <Link
                      className="text-sm font-semibold text-primary underline-offset-2 hover:underline"
                      to={evidenceWorkbenchPath("journal-entry", view.journalEntryId)}
                    >
                      {attachment.displayName}
                    </Link>
                    <p className="mt-1 text-xs text-muted-foreground">Added by {attachment.addedBy} on {attachment.addedAtUtc}</p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No evidence attached to this entry yet.</p>
            )}
          </CardContent>
        </Card>
      ) : null}

      <div className="flex flex-wrap gap-2">
        {runId ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("accountingTrialBalance", { runId })}>Back to Trial Balance</Link>
          </Button>
        ) : null}
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Open Journal Entries workbench</Link>
        </Button>
      </div>
    </div>
  );
}
