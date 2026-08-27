import { RefreshCcw, Search, ShieldAlert } from "lucide-react";
import { useMemo } from "react";
import { DenseDataTable, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import type {
  CorporateActionAcceptanceReceipt,
  CorporateActionInboxFilters,
  CorporateActionInboxRowModel,
  CorporateActionInboxViewModel
} from "@/screens/data-screen.corporate-action-inbox.view-model";
import type {
  CorporateActionBasisComparison,
  CorporateActionJournalPreview,
  CorporateActionLotPreview,
  CorporateActionReconciliationPreview
} from "@/types";

export const CORPORATE_ACTION_CASE_WORKSPACE_ID = "corporate-action-case-workspace";

const queueColumns: DenseDataTableColumn<CorporateActionInboxRowModel>[] = [
  {
    id: "event",
    label: "Event",
    render: (row) => (
      <span className="block min-w-0">
        <span className="flex flex-wrap items-center gap-1.5">
          <Badge variant={row.tone === "warning" ? "warning" : "outline"}>{row.actionType}</Badge>
          <span className="font-mono font-semibold text-foreground">{row.ticker}</span>
        </span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.securityId}</span>
      </span>
    )
  },
  {
    id: "deadline",
    label: "Ex-date / deadline",
    render: (row) => (
      <span className="block">
        <span className="block font-mono text-foreground">{row.exDateLabel}</span>
        <span className={row.daysUntilEx !== null && row.daysUntilEx < 0 ? "text-xs text-danger" : "text-xs text-muted-foreground"}>{row.countdownLabel}</span>
      </span>
    )
  },
  {
    id: "case",
    label: "Case / status",
    render: (row) => (
      <span className="block">
        <span className="block text-foreground">{row.statusLabel}</span>
        <span className="block max-w-48 truncate font-mono text-[11px] text-muted-foreground" title={row.caseIdLabel}>
          {row.caseIdLabel}
        </span>
      </span>
    )
  },
  {
    id: "assignment",
    label: "Assignment",
    render: (row) => (
      <span className="block">
        <span className="block text-foreground">{row.assignmentLabel}</span>
        <span className="text-xs text-muted-foreground">{row.permissionLabel}</span>
      </span>
    )
  },
  {
    id: "control",
    label: "Conflict / version",
    render: (row) => (
      <span className="block">
        <Badge variant={row.tone === "warning" ? "warning" : "outline"}>{row.conflictLabel}</Badge>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.versionLabel}</span>
      </span>
    )
  },
  {
    id: "value",
    label: "Terms",
    align: "right",
    render: (row) => (
      <span className="block">
        <span className="block font-mono tabular-nums text-foreground">{row.valueLabel}</span>
        <span className="text-xs text-muted-foreground">{row.consensusLabel}</span>
      </span>
    )
  }
];

function uniqueOptions(rows: CorporateActionInboxRowModel[], field: "statusLabel" | "assignmentLabel" | "conflictLabel") {
  return [...new Set(rows.map((row) => row[field]))].sort((left, right) => left.localeCompare(right));
}

function QueueFilters({
  filters,
  rows,
  onChange
}: {
  filters: CorporateActionInboxFilters;
  rows: CorporateActionInboxRowModel[];
  onChange: (filters: CorporateActionInboxFilters) => void;
}) {
  const statusOptions = useMemo(() => uniqueOptions(rows, "statusLabel"), [rows]);
  const assignmentOptions = useMemo(() => uniqueOptions(rows, "assignmentLabel"), [rows]);
  const conflictOptions = useMemo(() => uniqueOptions(rows, "conflictLabel"), [rows]);

  return (
    <fieldset className="grid gap-2 rounded-[2px] border border-border bg-secondary/10 p-3 md:grid-cols-4">
      <legend className="px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">Queue filters</legend>
      <label className="grid gap-1 text-xs font-medium text-muted-foreground">
        Search cases
        <Input
          value={filters.search}
          onChange={(event) => onChange({ ...filters, search: event.target.value })}
          leadingIcon={<Search className="h-4 w-4" />}
          placeholder="Ticker, security, case, source"
          aria-label="Search corporate action cases"
        />
      </label>
      <label className="grid gap-1 text-xs font-medium text-muted-foreground">
        Status
        <Select value={filters.status} onChange={(event) => onChange({ ...filters, status: event.target.value })}>
          <option value="All">All statuses</option>
          {statusOptions.map((option) => <option key={option} value={option}>{option}</option>)}
        </Select>
      </label>
      <label className="grid gap-1 text-xs font-medium text-muted-foreground">
        Assignment
        <Select value={filters.assignment} onChange={(event) => onChange({ ...filters, assignment: event.target.value })}>
          <option value="All">All assignments</option>
          {assignmentOptions.map((option) => <option key={option} value={option}>{option}</option>)}
        </Select>
      </label>
      <label className="grid gap-1 text-xs font-medium text-muted-foreground">
        Conflict
        <Select value={filters.conflict} onChange={(event) => onChange({ ...filters, conflict: event.target.value })}>
          <option value="All">All conflict states</option>
          {conflictOptions.map((option) => <option key={option} value={option}>{option}</option>)}
        </Select>
      </label>
    </fieldset>
  );
}

function UnavailableState({ title, reason }: { title: string; reason: string }) {
  return (
    <div className="rounded-[2px] border border-dashed border-border bg-secondary/10 px-3 py-2.5">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <ShieldAlert className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        {title}
      </div>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">{reason}</p>
    </div>
  );
}

function WorkspaceSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-[2px] border border-border bg-background p-3" aria-labelledby={`corporate-action-${title.toLowerCase().replaceAll(" ", "-")}`}>
      <h4 id={`corporate-action-${title.toLowerCase().replaceAll(" ", "-")}`} className="text-sm font-semibold text-foreground">{title}</h4>
      <div className="mt-2">{children}</div>
    </section>
  );
}

function SourceFacts({ row }: { row: CorporateActionInboxRowModel }) {
  const suppliedFacts = row.durableCase?.sourceFacts ?? [];
  const facts = suppliedFacts.length > 0 ? suppliedFacts : [
    { field: "Action type", value: row.actionType, source: row.winningSource, agreesWithWinner: true, evidenceId: row.sourceEvidenceReference },
    { field: "Ex-date", value: row.exDateLabel, source: row.winningSource, agreesWithWinner: true, evidenceId: row.sourceEvidenceReference },
    { field: "Record date", value: row.recordDateLabel, source: row.winningSource, agreesWithWinner: true, evidenceId: row.sourceEvidenceReference },
    { field: "Payable date", value: row.payableDateLabel, source: row.winningSource, agreesWithWinner: true, evidenceId: row.sourceEvidenceReference },
    { field: "Terms", value: row.valueLabel, source: row.winningSource, agreesWithWinner: true, evidenceId: row.sourceEvidenceReference }
  ];
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs" aria-label="Corporate action source facts and provenance">
        <thead>
          <tr>
            <th scope="col" className="border-b border-border px-2 py-1.5 text-left">Field</th>
            <th scope="col" className="border-b border-border px-2 py-1.5 text-left">Winning value</th>
            <th scope="col" className="border-b border-border px-2 py-1.5 text-left">Source</th>
            <th scope="col" className="border-b border-border px-2 py-1.5 text-left">Evidence</th>
          </tr>
        </thead>
        <tbody>
          {facts.map((fact, index) => (
            <tr key={`${fact.field}-${fact.source}-${index}`}>
              <th scope="row" className="border-b border-border/60 px-2 py-1.5 text-left font-medium">{fact.field}</th>
              <td className="border-b border-border/60 px-2 py-1.5 font-mono">{fact.value}</td>
              <td className="border-b border-border/60 px-2 py-1.5">{fact.source}</td>
              <td className="border-b border-border/60 px-2 py-1.5 font-mono">{fact.evidenceId ?? "Not supplied"}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-2 text-xs text-muted-foreground">
        Agreeing sources: {row.agreeingSources.join(", ") || "none"} · Dissenting sources: {row.dissentingSources.join(", ") || "none"}
      </p>
    </div>
  );
}

function BasisComparison({ rows }: { rows: CorporateActionBasisComparison[] }) {
  if (rows.length === 0) {
    return <UnavailableState title="Basis treatment not supplied" reason="GAAP, STAT, tax, and management treatment must come from the server-owned policy and preview service." />;
  }
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs" aria-label="Accounting basis comparison">
        <thead><tr>
          {["Basis", "Treatment", "Taxability", "Book value", "Gain / loss", "Holding period", "Status"].map((label) => (
            <th key={label} scope="col" className="border-b border-border px-2 py-1.5 text-left">{label}</th>
          ))}
        </tr></thead>
        <tbody>{rows.map((item) => (
          <tr key={item.basis}>
            <th scope="row" className="border-b border-border/60 px-2 py-1.5 text-left">{item.basis}</th>
            <td className="border-b border-border/60 px-2 py-1.5">{item.treatment}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{item.taxability}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{item.bookValueEffect}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{item.gainLossRecognition}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{item.holdingPeriodTreatment}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{item.status}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function LotPreview({ rows }: { rows: CorporateActionLotPreview[] }) {
  if (rows.length === 0) {
    return <UnavailableState title="Lot preview unavailable" reason="No versioned lot-mutation preview was supplied. Accepting the canonical fact does not change lots." />;
  }
  return (
    <ul className="grid gap-1.5 text-xs" aria-label="Lot mutation preview">
      {rows.map((lot) => (
        <li key={`${lot.basis}-${lot.lotId}`} className="rounded-[2px] border border-border px-2.5 py-2">
          <span className="font-mono font-semibold">{lot.lotId}</span> · {lot.basis} · {lot.operation}
          <span className="block text-muted-foreground">Quantity {lot.quantityBefore} → {lot.quantityAfter} · Book value {lot.bookValueBefore} → {lot.bookValueAfter}</span>
        </li>
      ))}
    </ul>
  );
}

function JournalPreview({ rows }: { rows: CorporateActionJournalPreview[] }) {
  if (rows.length === 0) {
    return <UnavailableState title="Journal preview unavailable" reason="No balanced, versioned journal projection was supplied. Accepting the canonical fact does not post accounting." />;
  }
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs" aria-label="Journal preview">
        <thead><tr>
          {["Basis", "Account", "Debit", "Credit", "Currency"].map((label) => <th key={label} scope="col" className="border-b border-border px-2 py-1.5 text-left">{label}</th>)}
        </tr></thead>
        <tbody>{rows.map((line) => (
          <tr key={line.lineId}>
            <td className="border-b border-border/60 px-2 py-1.5">{line.basis}</td>
            <th scope="row" className="border-b border-border/60 px-2 py-1.5 text-left">{line.account}</th>
            <td className="border-b border-border/60 px-2 py-1.5 text-right font-mono tabular-nums">{line.debit}</td>
            <td className="border-b border-border/60 px-2 py-1.5 text-right font-mono tabular-nums">{line.credit}</td>
            <td className="border-b border-border/60 px-2 py-1.5">{line.currency}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function Reconciliation({ rows }: { rows: CorporateActionReconciliationPreview[] }) {
  if (rows.length === 0) {
    return <UnavailableState title="Reconciliation not available" reason="Cash, security movement, lot, journal, and reporting controls are available only after the server creates them." />;
  }
  return (
    <dl className="grid gap-2 sm:grid-cols-2">
      {rows.map((control) => (
        <div key={control.control} className="rounded-[2px] border border-border px-2.5 py-2 text-xs">
          <dt className="font-semibold">{control.control} · {control.status}</dt>
          <dd className="mt-1 text-muted-foreground">Expected {control.expected} · Actual {control.actual} · Variance {control.variance}</dd>
        </div>
      ))}
    </dl>
  );
}

function CaseWorkspace({ row, panel }: { row: CorporateActionInboxRowModel; panel: CorporateActionInboxViewModel }) {
  const durableCase = row.durableCase;
  const acceptDisabledReason = row.acceptCanonicalFactDisabledReason;

  return (
    <DenseRowDetailPanel
      id={CORPORATE_ACTION_CASE_WORKSPACE_ID}
      ariaLabel={`Corporate action case workspace for ${row.actionType} on ${row.ticker}`}
      selectedSourceLabel="Selected from corporate action case queue"
      className="row-detail-panel h-fit min-w-0 space-y-3"
    >
      <div className="head">
        <div className="min-w-0">
          <div className="eyebrow-label">Corporate action case</div>
          <h3 className="mt-1 text-base font-semibold text-foreground">{row.actionType} · {row.ticker}</h3>
          <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{row.caseIdLabel}</p>
        </div>
        <Badge variant={row.tone === "warning" ? "warning" : "outline"}>{row.statusLabel}</Badge>
      </div>
      <dl className="grid gap-2 text-xs sm:grid-cols-2 xl:grid-cols-4">
        {[
          ["Proposal", row.proposalIdLabel],
          ["Version", row.versionLabel],
          ["Assignment", row.assignmentLabel],
          ["Conflict", row.conflictLabel],
          ["Permission", row.permissionLabel],
          ["Tenant / company", row.acceptanceScope ? `${row.acceptanceScope.tenantId} / ${row.acceptanceScope.companyId}` : "Not supplied"],
          ["Book / basis", row.acceptanceScope ? `${row.acceptanceScope.ledgerBookId ?? "Not supplied"} / ${row.acceptanceScope.accountingBasis ?? "Not supplied"}` : "Not supplied"],
          ["Source event", row.sourceEventLabel],
          ["Source observed", row.sourceObservedAtLabel],
          ["Source evidence", row.sourceEvidenceReference ?? "Not supplied"],
          ["Ex-date", `${row.exDateLabel} (${row.countdownLabel})`],
          ["Record date", row.recordDateLabel],
          ["Payable date", row.payableDateLabel]
        ].map(([label, value]) => (
          <div key={label} className="rounded-[2px] border border-border bg-secondary/10 px-2.5 py-2">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="mt-0.5 font-medium text-foreground">{value}</dd>
          </div>
        ))}
      </dl>
      <div className="flex flex-wrap gap-2 border-y border-border py-3" aria-label="Corporate action case actions">
        <Button
          type="button"
          size="sm"
          onClick={() => panel.requestAcceptance(row)}
          disabled={!row.canAcceptCanonicalFact || panel.acceptingKey !== null}
          disabledReason={acceptDisabledReason}
        >
          {panel.acceptingKey === row.rowId ? "Accepting canonical fact…" : "Accept canonical fact"}
        </Button>
        <p className="self-center text-xs text-muted-foreground">
          Election submission, treatment approval, and accounting posting remain outside this foundation workflow.
        </p>
      </div>
      {panel.acceptErrors[row.rowId] ? <StatusBanner role="alert" tone="danger" title="Canonical fact not accepted" detail={panel.acceptErrors[row.rowId]} /> : null}
      <WorkspaceSection title="Source facts and provenance"><SourceFacts row={row} /></WorkspaceSection>
      <WorkspaceSection title="Entitlement and election">
        {durableCase?.entitlement ? (
          <dl className="grid gap-2 text-xs sm:grid-cols-2">
            {Object.entries(durableCase.entitlement).map(([label, value]) => (
              <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="font-medium">{value}</dd></div>
            ))}
          </dl>
        ) : (
          <UnavailableState title="Entitlement not supplied" reason="Record-date positions, scope, election options, and custody evidence must be calculated and retained by the server." />
        )}
        {durableCase && durableCase.elections.length > 0 ? (
          <ul className="mt-2 grid gap-1 text-xs">{durableCase.elections.map((election, index) => (
            <li key={election.electionId ?? `${election.optionLabel}-${index}`} className="rounded-[2px] border border-border px-2.5 py-2">
              {election.optionLabel} · {election.quantity} · {election.status}
            </li>
          ))}</ul>
        ) : null}
      </WorkspaceSection>
      <WorkspaceSection title="Basis comparison"><BasisComparison rows={durableCase?.basisComparisons ?? []} /></WorkspaceSection>
      <WorkspaceSection title="Lot preview"><LotPreview rows={durableCase?.lotPreview ?? []} /></WorkspaceSection>
      <WorkspaceSection title="Journal preview"><JournalPreview rows={durableCase?.journalPreview ?? []} /></WorkspaceSection>
      <WorkspaceSection title="Reconciliation"><Reconciliation rows={durableCase?.reconciliation ?? []} /></WorkspaceSection>
      <WorkspaceSection title="History and proof">
        {durableCase && (durableCase.history.length > 0 || durableCase.proofReferences.length > 0) ? (
          <div className="grid gap-2 text-xs">
            <ol className="grid gap-1">
              {durableCase.history.map((entry) => (
                <li key={entry.eventId} className="rounded-[2px] border border-border px-2.5 py-2">
                  <span className="font-mono">{entry.atUtc}</span> · {entry.actor} · {entry.action}
                  {entry.evidenceId ? <span className="block font-mono text-muted-foreground">Evidence {entry.evidenceId}</span> : null}
                </li>
              ))}
            </ol>
            <p className="font-mono text-muted-foreground">Proof: {durableCase.proofReferences.join(", ") || "Not supplied"}</p>
          </div>
        ) : (
          <UnavailableState title="Case history and proof not supplied" reason="The current inbox contract supplies provider staging facts only. Durable transitions, evidence IDs, and proof hashes remain unavailable." />
        )}
      </WorkspaceSection>
    </DenseRowDetailPanel>
  );
}

function AcceptanceReceipt({ receipt, onClose }: { receipt: CorporateActionAcceptanceReceipt; onClose: () => void }) {
  const restatement = receipt.result.restatement;
  const sourceConflict = receipt.result.sourceConflict;
  const restatementPending = restatement?.evaluationStatus === "PendingPeriodValidation";
  const warningTitle = receipt.queueRefreshWarning
    ? "Canonical fact accepted; queue refresh incomplete"
    : sourceConflict
      ? "Canonical fact accepted; source conflict requires resolution"
      : restatementPending
        ? "Canonical fact accepted; period validation pending"
        : "Canonical fact accepted";
  return (
    <div className="space-y-2" role="status" aria-live="polite">
      <StatusBanner
        tone={receipt.queueRefreshWarning || sourceConflict || restatementPending ? "warning" : "success"}
        title={warningTitle}
        detail={`Corporate action ${receipt.result.corporateAction.corpActId} · audit ${receipt.result.audit.auditId} · actor ${receipt.result.audit.actor} · recorded ${receipt.result.audit.recordedAtUtc}${receipt.result.replayed ? " · idempotent replay" : ""}`}
      />
      {receipt.queueRefreshWarning ? <p className="text-xs text-warning">{receipt.queueRefreshWarning}</p> : null}
      {sourceConflict ? (
        <div className="rounded-[2px] border border-warning/50 bg-warning/5 px-3 py-2.5 text-xs">
          <div className="font-semibold">Open source conflict · {sourceConflict.field}</div>
          <p className="mt-1 text-muted-foreground">{sourceConflict.description}</p>
          <p className="mt-1 font-mono text-muted-foreground">
            Conflict {sourceConflict.conflictId} · {sourceConflict.candidates.map((candidate) => candidate.source).join(", ")}
          </p>
        </div>
      ) : null}
      {restatement ? (
        <div className="rounded-[2px] border border-border px-3 py-2.5 text-xs">
          <div className="font-semibold">
            {restatementPending
              ? `Period validation pending · ${restatement.candidates.length} candidate${restatement.candidates.length === 1 ? "" : "s"}`
              : restatement.restatementRequired
              ? `Restatement review required · ${restatement.candidates.length} candidate${restatement.candidates.length === 1 ? "" : "s"}`
              : "No restatement required"}
          </div>
          {restatement.candidates.length > 0 ? (
            <ul className="mt-1 grid gap-1 text-muted-foreground">
              {restatement.candidates.map((candidate) => (
                <li key={candidate.reportId}><span className="font-mono">{candidate.periodLabel}</span> · {candidate.summary} · {candidate.changedLines.length} changed line(s)</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">Restatement impact was not returned for this append.</p>
      )}
      <Button type="button" size="sm" variant="ghost" onClick={onClose}>Dismiss receipt</Button>
    </div>
  );
}

function AcceptCanonicalFactDialog({ panel }: { panel: CorporateActionInboxViewModel }) {
  const row = panel.pendingAcceptance;
  const titleId = "accept-canonical-fact-title";
  const descriptionId = "accept-canonical-fact-description";
  return (
    <Dialog open={Boolean(row)} onOpenChange={(open) => { if (!open) panel.cancelAcceptance(); }}>
      {row ? (
        <DialogContent className="max-w-lg" aria-labelledby={titleId} aria-describedby={descriptionId}>
          <DialogHeader>
            <DialogTitle id={titleId}>Accept {row.actionType} as a canonical fact?</DialogTitle>
            <DialogDescription id={descriptionId}>
              This appends the selected provider proposal for {row.ticker} to Security Master. It does not confirm entitlement,
              submit an election, approve GAAP, STAT, tax, or management treatment, change lots, or post journals. Server policy
              and concurrency are checked on submit.
            </DialogDescription>
          </DialogHeader>
          <dl className="grid gap-2 rounded-[2px] border border-border bg-secondary/10 p-3 text-xs sm:grid-cols-2">
            <div><dt className="text-muted-foreground">Winning source</dt><dd className="font-medium">{row.winningSource}</dd></div>
            <div><dt className="text-muted-foreground">Terms</dt><dd className="font-mono">{row.valueLabel}</dd></div>
            <div><dt className="text-muted-foreground">Ex-date</dt><dd className="font-mono">{row.exDateLabel}</dd></div>
            <div><dt className="text-muted-foreground">Version</dt><dd className="font-mono">{row.versionLabel}</dd></div>
            <div><dt className="text-muted-foreground">Scope</dt><dd className="font-mono">{row.acceptanceScope ? `${row.acceptanceScope.tenantId} / ${row.acceptanceScope.companyId}` : "Not supplied"}</dd></div>
            <div><dt className="text-muted-foreground">Idempotency key</dt><dd className="break-all font-mono">{panel.pendingAcceptanceRequest?.idempotencyKey ?? "Not supplied"}</dd></div>
          </dl>
          {panel.acceptErrors[row.rowId] ? <StatusBanner role="alert" tone="danger" title="Canonical fact not accepted" detail={panel.acceptErrors[row.rowId]} /> : null}
          <div className="flex flex-wrap justify-end gap-2">
            <Button type="button" variant="outline" onClick={panel.cancelAcceptance} disabled={panel.acceptingKey !== null}>Cancel</Button>
            <Button
              type="button"
              onClick={() => void panel.confirmAcceptance()}
              busy={panel.acceptingKey === row.rowId}
              busyLabel="Accepting canonical fact"
              aria-label={`Accept ${row.actionType} for ${row.ticker} as a canonical Security Master fact`}
            >
              Accept canonical fact
            </Button>
          </div>
        </DialogContent>
      ) : null}
    </Dialog>
  );
}

export function CorporateActionInboxRegion({ panel }: { panel: CorporateActionInboxViewModel }) {
  const allRows = panel.model?.rows ?? [];
  const conflictCount = allRows.filter((row) => row.conflictLabel !== "None" && row.conflictLabel !== "Not supplied").length;
  const unassignedCount = allRows.filter((row) => row.assignmentLabel === "Unassigned").length;
  const durableCaseCount = allRows.filter((row) => row.durableCase !== null || row.compactCase !== null).length;

  return (
    <section aria-labelledby="corporate-action-inbox-title" className="workspace-region corporate-action-inbox-region">
      <Card className="panel-surface">
        <CardHeader className="gap-3">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="corporate-action-inbox-title">
                Corporate action case queue
                {panel.model && panel.model.stagedCount > 0 ? ` (${panel.model.stagedCount})` : null}
              </CardTitle>
              <CardDescription>
                Review provider facts separately from entitlement, election, basis treatment, lot mutation, journal posting, and reconciliation.
                {panel.model ? ` ${panel.model.summary} Last ingest: ${panel.model.lastIngestLabel}.` : null}
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="outline" size="sm" disabled disabledReason={panel.bulkAcceptDisabledReason}>Bulk accept</Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => void panel.refresh()}
                disabled={panel.loading}
                aria-label="Refresh corporate action case queue"
              >
                <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
              </Button>
            </div>
          </div>
          <ToolbarStrip
            ariaLabel="Corporate action queue summary"
            items={[
              { id: "staged", label: "Staged", value: String(panel.model?.stagedCount ?? 0), active: true },
              { id: "conflicts", label: "Conflicts", value: String(conflictCount) },
              { id: "unassigned", label: "Unassigned", value: String(unassignedCount) },
              { id: "durable", label: "Durable cases", value: String(durableCaseCount) }
            ]}
          />
        </CardHeader>
        <CardContent className="space-y-4">
          {panel.acceptanceReceipt ? <AcceptanceReceipt receipt={panel.acceptanceReceipt} onClose={panel.clearAcceptanceReceipt} /> : null}
          {panel.error && !panel.model ? (
            <StatusBanner role="alert" tone="danger" title="Corporate action case queue unavailable" detail={panel.error} />
          ) : panel.error ? (
            <StatusBanner role="alert" tone="warning" title="Showing retained queue data" detail={panel.error} />
          ) : null}
          {panel.model?.hasPartialProviderFailure ? (
            <StatusBanner
              role="status"
              tone="warning"
              title="Provider ingest partially succeeded"
              detail={`${panel.model.errors.length} provider error${panel.model.errors.length === 1 ? "" : "s"} occurred; available proposals remain reviewable.`}
            />
          ) : null}
          {panel.loading && !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading corporate action case queue…</p>
          ) : panel.model ? (
            <>
              <QueueFilters filters={panel.filters} rows={allRows} onChange={panel.setFilters} />
              <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(24rem,0.85fr)]">
                <DenseDataTable
                  columns={queueColumns}
                  rows={panel.rows}
                  getRowId={(row) => row.rowId}
                  getRowAriaLabel={(row) => `${row.actionType} for ${row.ticker}, ex-date ${row.exDateLabel}, status ${row.statusLabel}, assignment ${row.assignmentLabel}, conflict ${row.conflictLabel}, version ${row.versionLabel}`}
                  getRowSelectAriaLabel={(row) => `Inspect corporate action case for ${row.actionType} on ${row.ticker}`}
                  getRowAriaControls={() => CORPORATE_ACTION_CASE_WORKSPACE_ID}
                  getRowAriaExpanded={(row) => panel.selectedRowKey === row.rowId}
                  getRowTypeaheadText={(row) => `${row.ticker} ${row.actionType} ${row.caseIdLabel}`}
                  onRowSelect={(row) => panel.selectRow(row.rowId)}
                  selectedRowId={panel.selectedRowKey}
                  emptyText={allRows.length === 0 ? panel.model.summary : "No corporate action cases match the current filters."}
                  ariaLabel="Corporate action case queue"
                  tableId="corporate-action-case-queue"
                  caption="Corporate action proposals and durable processing cases; use keyboard row navigation to inspect the case workspace."
                />
                {panel.selectedRow ? (
                  <CaseWorkspace row={panel.selectedRow} panel={panel} />
                ) : (
                  <DenseRowDetailPanel
                    id={CORPORATE_ACTION_CASE_WORKSPACE_ID}
                    ariaLabel="No corporate action case selected"
                    selectedSourceLabel="No selected corporate action case"
                    className="row-detail-panel h-fit min-w-0"
                  >
                    <div className="eyebrow-label">Corporate action case</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">No case selected</h3>
                    <p className="mt-2 text-sm text-muted-foreground">Adjust the filters or select a queue row to inspect source facts and processing evidence.</p>
                  </DenseRowDetailPanel>
                )}
              </div>
            </>
          ) : null}
          {panel.model && panel.model.errors.length > 0 ? (
            <details className="rounded-[2px] border border-border px-3 py-2">
              <summary className="cursor-pointer text-sm font-semibold">Provider errors last run ({panel.model.errors.length})</summary>
              <ul className="mt-2 grid gap-1">
                {panel.model.errors.map((message, index) => (
                  <li key={`${message}-${index}`} className="font-mono text-xs text-muted-foreground">{message}</li>
                ))}
              </ul>
            </details>
          ) : null}
        </CardContent>
      </Card>
      <AcceptCanonicalFactDialog panel={panel} />
    </section>
  );
}
