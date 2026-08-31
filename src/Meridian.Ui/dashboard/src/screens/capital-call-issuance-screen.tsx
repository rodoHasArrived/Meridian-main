import { Fragment } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, HandCoins, Plus, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Callout } from "@/components/ui/callout";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { RegionErrorState } from "@/components/ui/error-boundary";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { StatusBanner } from "@/components/ui/status-banner";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  presentCapitalCallRunOutcome,
  presentCreatedDrafts,
  presentRunLevelAssessments,
  presentSkips,
  useCapitalCallIssuanceViewModel,
  type CapitalCallAssessmentView,
  type CapitalCallIssuanceViewModel
} from "@/screens/capital-call-issuance-screen.view-model";

const EVIDENCE_QUALITY_BADGE: Record<CapitalCallAssessmentView["quality"], "success" | "warning" | "danger"> = {
  High: "success",
  Medium: "warning",
  Low: "danger"
};

function FieldRow({ label, htmlFor, children }: { label: string; htmlFor: string; children: React.ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <Label htmlFor={htmlFor}>{label}</Label>
      {children}
    </div>
  );
}

function AssessmentDetails({ assessment }: { assessment: CapitalCallAssessmentView }) {
  return (
    <div className="space-y-1">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={EVIDENCE_QUALITY_BADGE[assessment.quality]}>
          Evidence {assessment.quality}
        </Badge>
        <span className="text-xs text-muted-foreground">Confidence {assessment.confidenceLabel}</span>
        {assessment.requiresInvestigation ? (
          <Badge variant="danger">Requires investigation</Badge>
        ) : null}
      </div>
      <p className="text-xs text-muted-foreground">{assessment.summary}</p>
      {assessment.reasons.length > 0 ? (
        <ul className="list-disc space-y-1 pl-5 text-xs text-warning">
          {assessment.reasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function RunResultSection({ view }: { view: CapitalCallIssuanceViewModel }) {
  if (view.submitError) {
    return (
      <RegionErrorState
        message={view.submitError.summary}
        detail={view.submitError.details.length > 0 ? view.submitError.details.join(" ") : null}
      />
    );
  }

  if (!view.result) {
    return null;
  }

  const outcome = presentCapitalCallRunOutcome(view.result);
  const createdDrafts = presentCreatedDrafts(view.result);
  const runAssessments = presentRunLevelAssessments(view.result);
  const skips = presentSkips(view.result);

  return (
    <div className="space-y-3" data-testid="capital-call-run-result">
      <StatusBanner tone={outcome.tone} title={outcome.title} detail={outcome.detail} />

      {outcome.blockers.length > 0 ? (
        <div
          role="alert"
          className="rounded-[2px] border border-danger/30 bg-danger/10 px-3.5 py-3 text-sm"
          data-testid="capital-call-blockers"
        >
          <div className="font-semibold text-danger">Server-reported reasons</div>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-danger/90">
            {outcome.blockers.map((blocker) => (
              <li key={blocker}>{blocker}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {runAssessments.map((assessment, index) => (
        <Fragment key={`${assessment.summary}-${index}`}>
          <AssessmentDetails assessment={assessment} />
        </Fragment>
      ))}

      {createdDrafts.length > 0 ? (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle>Created issuance drafts</CardTitle>
            <CardDescription>
              Queued in the{" "}
              <Link
                to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}
                className="underline hover:text-foreground"
              >
                manual-journal approval queue
              </Link>
              ; every draft still needs human submit and approval before posting.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="space-y-3" data-testid="capital-call-created-drafts">
              {createdDrafts.map((draft) => (
                <li key={draft.journalEntryId} className="rounded border border-border/60 bg-secondary/15 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="text-sm font-semibold text-foreground">{draft.memo}</span>
                    <span className="font-mono text-sm text-foreground">{draft.amountLabel}</span>
                  </div>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <Badge variant="outline">{draft.status}</Badge>
                    {draft.investorId ? <span>Investor {draft.investorId}</span> : null}
                    {draft.capitalAccountId ? <span>Capital account {draft.capitalAccountId}</span> : null}
                    <span className="font-mono">{draft.journalEntryId}</span>
                  </div>
                  {draft.assessment ? (
                    <div className="mt-2 border-t border-border/60 pt-2">
                      <AssessmentDetails assessment={draft.assessment} />
                    </div>
                  ) : null}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}

      {skips.length > 0 ? (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle>Skipped</CardTitle>
            <CardDescription>Events the run did not turn into a new draft, with the server's reason.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground" data-testid="capital-call-skips">
              {skips.map((skip, index) => (
                <li key={`${skip.subject}-${index}`}>
                  <span className="font-mono text-xs">{skip.subject}</span>: {skip.reason}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

export function CapitalCallIssuanceScreen() {
  const view = useCapitalCallIssuanceViewModel();

  return (
    <div className="space-y-4 p-4" data-testid="capital-call-issuance-screen">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <HandCoins className="size-5 text-muted-foreground" aria-hidden />
          <div>
            <h1 className="text-lg font-semibold text-foreground">Capital Call Issuance</h1>
            <p className="text-sm text-muted-foreground">
              Plan a fund-level capital call over the attested commitment register and queue governed
              per-LP issuance drafts for approval. This surface never posts.
            </p>
          </div>
        </div>
        <Link
          to={WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts}
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" aria-hidden /> Capital accounts
        </Link>
      </div>

      <Callout tone="info" title="How this run is governed">
        The server recomputes each commitment's called-to-date basis from posted private-capital fund
        events — it never trusts a caller-supplied uncalled amount — and lands drafts in the
        manual-journal approval queue. A run that cannot be corroborated is blocked with explicit
        reasons instead of drafting numbers.
      </Callout>

      <Card>
        <CardHeader className="pb-2">
          <CardTitle>Call terms</CardTitle>
          <CardDescription>
            Fund, book, and call parameters. The ledger book is required — without it drafts land in
            the queue flagged book-missing.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            <FieldRow label="Fund profile id" htmlFor="cci-fund-profile">
              <Input
                id="cci-fund-profile"
                value={view.form.fundProfileId}
                onChange={(event) => view.updateField("fundProfileId", event.target.value)}
                placeholder="fund-profile identifier"
              />
            </FieldRow>
            <FieldRow label="Ledger book id (GUID)" htmlFor="cci-ledger-book">
              <Input
                id="cci-ledger-book"
                value={view.form.ledgerBookId}
                onChange={(event) => view.updateField("ledgerBookId", event.target.value)}
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </FieldRow>
            <FieldRow label="Currency (ISO)" htmlFor="cci-currency">
              <Input
                id="cci-currency"
                value={view.form.currency}
                onChange={(event) => view.updateField("currency", event.target.value)}
                placeholder="USD"
                maxLength={3}
              />
            </FieldRow>
            <FieldRow label="Capital-call id" htmlFor="cci-call-id">
              <Input
                id="cci-call-id"
                value={view.form.callId}
                onChange={(event) => view.updateField("callId", event.target.value)}
                placeholder="call-2026-03"
              />
            </FieldRow>
            <FieldRow label="Amount to call" htmlFor="cci-amount">
              <Input
                id="cci-amount"
                inputMode="decimal"
                value={view.form.amountToCall}
                onChange={(event) => view.updateField("amountToCall", event.target.value)}
                placeholder="No default — enter the called amount"
              />
            </FieldRow>
            <FieldRow label="Allocation basis" htmlFor="cci-allocation-basis">
              <select
                id="cci-allocation-basis"
                className="rounded border bg-background px-2 py-1.5 text-sm text-foreground"
                value={view.form.allocationBasis}
                onChange={(event) => view.updateField(
                  "allocationBasis",
                  event.target.value === "pro-rata-total-commitment"
                    ? "pro-rata-total-commitment"
                    : "pro-rata-uncalled"
                )}
              >
                <option value="pro-rata-uncalled">Pro-rata by uncalled commitment</option>
                <option value="pro-rata-total-commitment">Pro-rata by total commitment</option>
              </select>
            </FieldRow>
            <FieldRow label="Notice date" htmlFor="cci-notice-date">
              <Input
                id="cci-notice-date"
                type="date"
                value={view.form.noticeDate}
                onChange={(event) => view.updateField("noticeDate", event.target.value)}
              />
            </FieldRow>
            <FieldRow label="Due date" htmlFor="cci-due-date">
              <Input
                id="cci-due-date"
                type="date"
                value={view.form.dueDate}
                onChange={(event) => view.updateField("dueDate", event.target.value)}
              />
            </FieldRow>
            <FieldRow label="Period id (optional)" htmlFor="cci-period">
              <Input
                id="cci-period"
                value={view.form.periodId}
                onChange={(event) => view.updateField("periodId", event.target.value)}
              />
            </FieldRow>
            <FieldRow label="Entity id (optional)" htmlFor="cci-entity">
              <Input
                id="cci-entity"
                value={view.form.entityId}
                onChange={(event) => view.updateField("entityId", event.target.value)}
              />
            </FieldRow>
            <FieldRow label="Purpose (optional)" htmlFor="cci-purpose">
              <Input
                id="cci-purpose"
                value={view.form.purpose}
                onChange={(event) => view.updateField("purpose", event.target.value)}
                placeholder="e.g. Follow-on investment"
              />
            </FieldRow>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Commitment register</CardTitle>
              <CardDescription>
                One attested line per LP. Each line must carry a retained commitment-register evidence
                link — the server blocks the run without it.
              </CardDescription>
            </div>
            <Button type="button" size="sm" variant="outline" onClick={view.addCommitmentRow}>
              <Plus className="h-3.5 w-3.5" aria-hidden="true" /> Add commitment
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          {view.form.commitments.map((row, index) => (
            <div
              key={row.key}
              className="rounded border border-border/60 bg-secondary/15 px-3 py-2"
              data-testid={`commitment-row-${index}`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  Line {index + 1}
                </span>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  onClick={() => view.removeCommitmentRow(row.key)}
                  aria-label={`Remove commitment line ${index + 1}`}
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Remove
                </Button>
              </div>
              <div className="mt-2 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                <FieldRow label="Commitment id" htmlFor={`cci-row-${index}-commitment`}>
                  <Input
                    id={`cci-row-${index}-commitment`}
                    value={row.commitmentId}
                    onChange={(event) => view.updateCommitment(row.key, "commitmentId", event.target.value)}
                  />
                </FieldRow>
                <FieldRow label="Capital account id" htmlFor={`cci-row-${index}-capital-account`}>
                  <Input
                    id={`cci-row-${index}-capital-account`}
                    value={row.capitalAccountId}
                    onChange={(event) => view.updateCommitment(row.key, "capitalAccountId", event.target.value)}
                  />
                </FieldRow>
                <FieldRow label="Investor id" htmlFor={`cci-row-${index}-investor`}>
                  <Input
                    id={`cci-row-${index}-investor`}
                    value={row.investorId}
                    onChange={(event) => view.updateCommitment(row.key, "investorId", event.target.value)}
                  />
                </FieldRow>
                <FieldRow label="Total commitment" htmlFor={`cci-row-${index}-total`}>
                  <Input
                    id={`cci-row-${index}-total`}
                    inputMode="decimal"
                    value={row.totalCommitment}
                    onChange={(event) => view.updateCommitment(row.key, "totalCommitment", event.target.value)}
                    placeholder="No default — attested total"
                  />
                </FieldRow>
                <FieldRow label="Commitment date" htmlFor={`cci-row-${index}-date`}>
                  <Input
                    id={`cci-row-${index}-date`}
                    type="date"
                    value={row.commitmentDate}
                    onChange={(event) => view.updateCommitment(row.key, "commitmentDate", event.target.value)}
                  />
                </FieldRow>
                <FieldRow label="Evidence link" htmlFor={`cci-row-${index}-evidence`}>
                  <Input
                    id={`cci-row-${index}-evidence`}
                    value={row.evidenceLink}
                    onChange={(event) => view.updateCommitment(row.key, "evidenceLink", event.target.value)}
                    placeholder="Retained commitment-register reference"
                  />
                </FieldRow>
              </div>
            </div>
          ))}
        </CardContent>
      </Card>

      {view.validationIssues.length > 0 ? (
        <div
          role="alert"
          className="rounded-[2px] border border-danger/30 bg-danger/10 px-3.5 py-3 text-sm"
          data-testid="capital-call-validation-issues"
        >
          <div className="font-semibold text-danger">Fix these before the call can be drafted</div>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-danger/90">
            {view.validationIssues.map((issue) => (
              <li key={issue}>{issue}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {view.armedNotice ? (
        <Callout tone="warning" title="Confirm governed drafting">
          {view.armedNotice}
        </Callout>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant={view.armed ? "default" : "outline"}
          busy={view.busy}
          busyLabel="Queueing issuance drafts"
          onClick={() => void view.submit()}
        >
          {view.submitLabel}
        </Button>
        {view.armed ? (
          <Button type="button" variant="ghost" onClick={view.disarm}>
            Cancel
          </Button>
        ) : null}
      </div>

      <RunResultSection view={view} />
    </div>
  );
}
