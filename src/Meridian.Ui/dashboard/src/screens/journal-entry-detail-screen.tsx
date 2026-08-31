import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { formatCurrency } from "@/lib/format";
import { LedgerTable, type LedgerRow } from "@/components/accounting/LedgerTable";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { getManualJournalEntryWorkbench, getRunLedgerJournal } from "@/lib/api";
import { getLedgerBooks, getLedgerPeriodJournalEntries } from "@/lib/ledger-reports-api";
import { toLedgerJournalLine } from "@/screens/accounting-screen.posted-ledger.view-model";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import {
  buildJournalEntryDetailViewState,
  type JournalEntryDetailSourceKind
} from "@/screens/journal-entry-detail-screen.view-model";
import type { LedgerJournalLine, LedgerPostedJournalEntry, ManualJournalEntryDraft } from "@/types";

const JOURNAL_ENTRY_SOURCE_LABELS: Record<JournalEntryDetailSourceKind, string> = {
  "manual-draft": "Manual journal workbench",
  "posted-journal": "Governed posted journal",
  "run-summary": "Run ledger summary",
  none: "Unknown"
};

export function JournalEntryDetailScreen() {
  const [searchParams] = useSearchParams();
  const journalEntryId = searchParams.get("journalEntryId") ?? "";
  const runId = searchParams.get("runId");
  // Trial Balance links posted entries with ?periodId=. Those live in the governed journal, not
  // in the manual workbench and not in any strategy run, so without this the fund's own postings
  // were the one kind of entry this screen could not open.
  const periodId = searchParams.get("periodId");

  const [draft, setDraft] = useState<ManualJournalEntryDraft | null>(null);
  const [journalLine, setJournalLine] = useState<LedgerJournalLine | null>(null);
  const [postedEntry, setPostedEntry] = useState<LedgerPostedJournalEntry | null>(null);
  // Posted amounts are in the ledger book's base currency, which the posted payload does not
  // carry — only its ledgerBookId. Resolved from the books list so a EUR or GBP book is not
  // labelled in dollars.
  const [postedCurrency, setPostedCurrency] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [reloadVersion, setReloadVersion] = useState(0);

  useEffect(() => {
    if (!journalEntryId) {
      setDraft(null);
      setJournalLine(null);
      setPostedEntry(null);
      setPostedCurrency(null);
      setErrorText(null);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setErrorText(null);
    // These three sources are mutually exclusive. Clearing only on an empty id or an error left a
    // previous period's posting on screen when the same mounted route moved to a run-scoped
    // entry, and buildJournalEntryDetailViewState prefers postedEntry.
    setDraft(null);
    setJournalLine(null);
    setPostedEntry(null);
    setPostedCurrency(null);

    // A period scope means the governed journal is the authority. Nesting it inside the
    // manual-workbench continuation meant an unavailable or forbidden workbench reported the
    // posted entry as unavailable without ever asking the period route.
    if (periodId) {
      getLedgerPeriodJournalEntries(periodId)
        .then((entries) => {
          if (cancelled) return;
          const matched = entries.find((entry) => entry.journalEntryId === journalEntryId) ?? null;
          setPostedEntry(matched);
          setJournalLine(matched ? toLedgerJournalLine(matched) : null);

          // Fire-and-forget, deliberately not returned into this chain. The currency only decides
          // how the amounts are labelled, so awaiting it here would hold `loading` — and therefore
          // every piece of posting detail already in hand — behind a books request that may be
          // slow or never settle.
          if (matched?.ledgerBookId) {
            void getLedgerBooks()
              .then((books) => {
                if (cancelled) return;
                const book = books.find((candidate) => candidate.ledgerBookId === matched.ledgerBookId);
                setPostedCurrency(book?.baseCurrency ?? null);
              })
              .catch(() => undefined);
          }
        })
        .catch(() => {
          if (!cancelled) {
            setErrorText("The posted journal for this period could not be loaded. No posting detail is shown until the source responds.");
          }
        })
        .finally(() => {
          if (!cancelled) {
            setLoading(false);
          }
        });

      return () => {
        cancelled = true;
      };
    }

    // Unlike periodId, runId does not choose the source: a manual draft is routinely reached with
    // a runId on the query for the "Back to Run Ledger" link, and the draft is still the record.
    // So the workbench is asked first and a matching draft still wins. What must not happen is the
    // workbench deciding the whole screen when it refuses: it authorizes on the manual-journal
    // permissions, a ViewStrategies-only operator is refused there, and the run journal route does
    // authorize them. Treating that refusal as "no draft" rather than as fatal lets the run lookup
    // below answer for exactly those operators, who previously saw an unavailable-workbench error
    // for a run journal they could in fact read.
    let workbenchUnavailable = false;

    getManualJournalEntryWorkbench({})
      .then((workbench) => workbench.drafts.find((candidate) => candidate.journalEntryId === journalEntryId) ?? null)
      .catch(() => {
        workbenchUnavailable = true;
        return null;
      })
      .then((matchedDraft) => {
        if (cancelled) {
          return;
        }

        setDraft(matchedDraft);

        if (matchedDraft) {
          setLoading(false);
          return;
        }

        if (runId) {
          return getRunLedgerJournal(runId)
            .then((lines) => {
              if (!cancelled) {
                setJournalLine(lines.find((line) => line.journalEntryId === journalEntryId) ?? null);
              }
            })
            .catch(() => {
              if (!cancelled) {
                setErrorText("The run journal could not be loaded. No posting detail is shown until the source responds.");
              }
            })
            .finally(() => {
              if (!cancelled) {
                setLoading(false);
              }
            });
        }

        // Only report the workbench as the failure when it actually failed and nothing else could
        // answer. A workbench that responded and simply held no such draft is not an error.
        if (workbenchUnavailable) {
          setJournalLine(null);
          setPostedEntry(null);
          setErrorText("The journal-entry record could not be loaded from the governed workbench. No posting detail is shown until the source responds.");
        }

        setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [journalEntryId, periodId, reloadVersion, runId]);

  if (!journalEntryId) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Journal Entry Detail</CardTitle>
          <CardDescription>Open this page from a trial balance, ledger, or reconciliation row to inspect one posting.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          <Button asChild size="sm">
            <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Open Journal Entries</Link>
          </Button>
          <Button asChild size="sm" variant="outline">
            <Link to={WORKSTATION_ROUTE_CATALOG.accountingLedger}>Open Ledger Explorer</Link>
          </Button>
        </CardContent>
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
          <CardDescription>Looking up the selected posting.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (errorText) {
    return (
      <Card className="panel-surface" role="region" aria-labelledby="journal-entry-error-title">
        <CardHeader>
          <CardTitle id="journal-entry-error-title">Journal entry unavailable</CardTitle>
          <CardDescription>The selected posting reference could not be loaded.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <StatusBanner role="alert" tone="danger" title="Source unavailable" detail={errorText} />
          <div className="flex flex-wrap gap-2">
            <Button size="sm" onClick={() => setReloadVersion((current) => current + 1)}>Retry</Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Back to Journal Entries</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  const view = buildJournalEntryDetailViewState({ journalEntryId, draft, journalLine, postedEntry, postedCurrency });
  const technicalSummaryFields = view.summaryFields.filter((field) => ["Journal entry", "Fund", "Ledger book"].includes(field.label));
  const operationalSummaryFields = view.summaryFields.filter((field) => !technicalSummaryFields.includes(field));

  const lineRows: LedgerRow[] = view.lines.map((line) => ({
    date: line.date ?? draft?.accountingDate ?? "",
    ref: "Current entry",
    memo: line.description ?? "",
    account: line.account,
    debit: line.debit,
    credit: line.credit
  }));

  return (
    <ScreenLayout
      title={view.title}
      scope="Journal entry detail"
      actions={<Badge variant={view.statusTone} dot>{view.statusLabel}</Badge>}
    >
      <Card className="panel-surface">
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
                {operationalSummaryFields.map((field) => (
                  <div key={field.label}>
                    <dt className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                    <dd className="mt-1 text-sm text-foreground">{field.label === "Accounting date" ? presentJournalDate(field.value) : field.label === "Posted at" ? formatDateTimeLabel(field.value) : field.value}</dd>
                  </div>
                ))}
              </dl>
              {technicalSummaryFields.length > 0 ? (
                <TechnicalDetails label="Journal identifiers" className="mt-4">
                  <dl className="grid gap-2 sm:grid-cols-2">
                    {technicalSummaryFields.map((field) => (
                      <JournalEntryFact key={field.label} label={field.label} value={field.value} />
                    ))}
                  </dl>
                </TechnicalDetails>
              ) : null}
            </>
          )}
        </CardContent>
      </Card>

      {view.dataCompleteness === "summary-only" ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Posting summary</CardTitle>
            <CardDescription>System-posted totals from the run ledger. Line-level detail lives in the run&apos;s Ledger Explorer.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <dl className="grid gap-3 sm:grid-cols-2">
              <JournalEntryFact label="Total debits" value={formatCurrency(view.totalDebits)} />
              <JournalEntryFact label="Total credits" value={formatCurrency(view.totalCredits)} />
            </dl>
            {runId ? (
              <Button asChild size="sm" variant="outline">
                <Link to={workstationRouteWithQuery("strategyRunLedger", { runId })}>Open in Run Ledger</Link>
              </Button>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

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

      {/* Lifecycle, Evidence and Approval belong to the manual workbench's workflow. A governed
          posted entry has none of them, and gating on completeness rendered all three for posted
          entries with empty arrays and an "Approval pending" that can never resolve. */}
      {view.sourceKind === "manual-draft" ? (
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
                      <span className="text-xs text-muted-foreground">{formatDateTimeLabel(transition.recordedAtUtc)}</span>
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

      {view.sourceKind === "manual-draft" ? (
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
                    <p className="mt-1 text-xs text-muted-foreground">Added by {attachment.addedBy} on {formatDateTimeLabel(attachment.addedAtUtc)}</p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No evidence attached to this entry yet.</p>
            )}
          </CardContent>
        </Card>
      ) : null}

      {view.dataCompleteness !== "not-found" ? (
        <section className="grid gap-4 xl:grid-cols-3">
          {view.sourceKind === "manual-draft" ? (
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Approval</CardTitle>
              <CardDescription>Review who prepared, reviewed, and approved this posting.</CardDescription>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-2">
                <JournalEntryFact label="Prepared by" value={fieldValue(view.summaryFields, "Prepared by") ?? "System"} />
                <JournalEntryFact label="Reviewed by" value={view.lifecycle[0]?.actor ?? "Review pending"} />
                <JournalEntryFact label="Approved by" value={view.lifecycle.find((item) => item.label.includes("Approve"))?.actor ?? "Approval pending"} />
              </dl>
              {view.lifecycle.some((item) => item.label.includes("Approve")) ? (
                <TechnicalDetails label="Approval reference" className="mt-3">
                  <JournalEntryFact label="Approval ID" value={view.lifecycle.find((item) => item.label.includes("Approve"))?.transitionId ?? "Not approved"} />
                </TechnicalDetails>
              ) : null}
            </CardContent>
          </Card>
          ) : null}

          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Source lineage</CardTitle>
              <CardDescription>Trace where this entry came from before using it in close or reporting.</CardDescription>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-2">
                <JournalEntryFact label="Source" value={JOURNAL_ENTRY_SOURCE_LABELS[view.sourceKind]} />
                <JournalEntryFact label="Reversal of" value="No reversal retained" />
                <JournalEntryFact label="Rebooked from" value="No rebook lineage retained" />
              </dl>
              <TechnicalDetails label="Lineage references" className="mt-3">
                <dl className="grid gap-2">
                  <JournalEntryFact label="Journal entry" value={view.journalEntryId} />
                  <JournalEntryFact label="Run / case" value={runId ?? "No run context"} />
                </dl>
              </TechnicalDetails>
            </CardContent>
          </Card>

          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Actions</CardTitle>
              <CardDescription>Use routed actions for evidence, cloning, review, export, or controlled reversal.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="flex flex-wrap gap-2">
                <Button type="button" size="sm" variant="outline" disabled disabledReason="Reversals are created from the Journal Entries workbench.">
                  Reverse
                </Button>
                <Button asChild size="sm" variant="outline">
                  <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Clone</Link>
                </Button>
                <Button asChild size="sm" variant="outline">
                  <Link to={evidenceWorkbenchPath("journal-entry", view.journalEntryId)}>Attach evidence</Link>
                </Button>
                <Button type="button" size="sm" variant="outline" disabled disabledReason="Review requests are submitted from the approval workflow.">
                  Request review
                </Button>
                <Button type="button" size="sm" variant="outline" disabled disabledReason="Exports are generated from retained report or workbench output.">
                  Export
                </Button>
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      <div className="flex flex-wrap gap-2">
        {runId ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("strategyRunLedger", { runId })}>Back to Run Ledger</Link>
          </Button>
        ) : null}
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Open Journal Entries workbench</Link>
        </Button>
      </div>
    </ScreenLayout>
  );
}

function JournalEntryFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[minmax(0,0.65fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
      <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="break-words text-right text-sm text-foreground">{value}</dd>
    </div>
  );
}

function fieldValue(fields: { label: string; value: string }[], label: string): string | null {
  return fields.find((field) => field.label === label)?.value ?? null;
}

function presentJournalDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
}
