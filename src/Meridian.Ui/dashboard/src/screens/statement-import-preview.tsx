import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  STATEMENT_FORMAT_DRIFT_ISSUE_CODE,
  statementColumnConfidenceBadgeVariant,
  statementIssueBadgeVariant
} from "@/screens/statement-import-panel.view-model";
import type { StatementImportPreview } from "@/types";

export interface StatementImportPreviewDetailsProps {
  onSelectKind: (kind: string) => void;
  preview: StatementImportPreview;
  selectedKind: string | null;
}

export function StatementImportPreviewDetails({
  onSelectKind,
  preview,
  selectedKind
}: StatementImportPreviewDetailsProps) {
  const selectedKindSummary = preview.kindSummaries.find((summary) => summary.kind === selectedKind)
    ?? preview.kindSummaries[0]
    ?? null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Preview: {preview.fileName}</CardTitle>
        <CardDescription>
          {preview.connectorDisplayName} parsed {preview.recordCount} records. {preview.nextAction}
        </CardDescription>
        <div className="mt-1">
          <Badge variant={preview.status === "ReadyToImport" ? "success" : "warning"}>
            {preview.status}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <StatementEvidenceSummary preview={preview} />
        <StatementColumnMappingTable preview={preview} />
        <StatementKindSummarySection
          preview={preview}
          selectedKind={selectedKindSummary?.kind ?? null}
          onSelectKind={onSelectKind}
        />
        <StatementIssueList preview={preview} />
      </CardContent>
    </Card>
  );
}

function StatementEvidenceSummary({ preview }: { preview: StatementImportPreview }) {
  const accounts = preview.accountSnapshots ?? [];
  const subtypes = preview.activitySubtypeSummaries ?? [];
  const completeness = preview.activityCompleteness ?? [];
  if (accounts.length === 0 && subtypes.length === 0 && completeness.length === 0 &&
      !preview.taxLotCount && !preview.borrowPositionCount) return null;

  return (
    <div className="flex flex-col gap-3 rounded-[2px] border border-border bg-muted/15 p-3" aria-label="Canonical statement evidence">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs font-semibold">Canonical provider evidence</p>
        <div className="flex flex-wrap gap-1.5">
          <Badge variant="outline">Tax lots {preview.taxLotCount ?? 0}</Badge>
          <Badge variant="outline">Borrow positions {preview.borrowPositionCount ?? 0}</Badge>
          {completeness.map((cursor, index) => (
            <Badge key={`${cursor.lastEventId ?? "cursor"}-${index}`} variant={cursor.isComplete ? "success" : "danger"}>
              Activity {cursor.isComplete ? "complete" : "incomplete"} · {cursor.sourceRecordCount} records / {cursor.pageCount} pages
            </Badge>
          ))}
        </div>
      </div>
      {accounts.map((account) => (
        <div key={`${account.providerId}:${account.accountId}`} className="grid gap-2 text-xs sm:grid-cols-2 lg:grid-cols-4">
          <EvidenceMetric label="Account" value={`${account.providerId} · ${account.accountId}`} />
          <EvidenceMetric label="Regime" value={account.marginRegime} />
          <EvidenceMetric label="Provider maintenance" value={formatAmount(account.maintenanceMargin, account.currency)} />
          <EvidenceMetric label="Provider excess" value={formatAmount(account.excessLiquidity, account.currency)} />
          {account.restrictions.length ? <p className="text-destructive sm:col-span-2 lg:col-span-4">{account.restrictions.join("; ")}</p> : null}
        </div>
      ))}
      {subtypes.length ? (
        <div className="flex flex-wrap gap-1.5">
          {subtypes.map((item) => <Badge key={`${item.category}:${item.subtype}`} variant="outline">{item.category} · {item.subtype} {item.recordCount}</Badge>)}
        </div>
      ) : null}
    </div>
  );
}

function EvidenceMetric({ label, value }: { label: string; value: string }) {
  return <div><p className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p><p className="mt-1 font-mono">{value}</p></div>;
}

function formatAmount(value: number | null, currency: string) {
  return value == null ? "Not reported" : new Intl.NumberFormat(undefined, { style: "currency", currency }).format(value);
}

function StatementColumnMappingTable({ preview }: { preview: StatementImportPreview }) {
  if (preview.columnMappings.length === 0) {
    return null;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs" aria-label="Statement column mappings">
        <caption className="sr-only">Detected statement columns mapped onto canonical Meridian fields</caption>
        <thead>
          <tr className="border-b border-border text-left font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
            <th scope="col" className="px-2 py-1.5">Source column</th>
            <th scope="col" className="px-2 py-1.5">Canonical field</th>
            <th scope="col" className="px-2 py-1.5">Confidence</th>
          </tr>
        </thead>
        <tbody>
          {preview.columnMappings.map((mapping) => (
            <tr key={mapping.sourceColumn} className="border-b border-border/60">
              <td className="px-2 py-1.5 font-mono">{mapping.sourceColumn}</td>
              <td className="px-2 py-1.5 font-mono">{mapping.canonicalField ?? "—"}</td>
              <td className="px-2 py-1.5">
                <Badge
                  variant={statementColumnConfidenceBadgeVariant(mapping.confidence)}
                  title={`${mapping.rationale} (score ${mapping.score.toFixed(2)})`}
                >
                  {mapping.confidence}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatementKindSummarySection({
  onSelectKind,
  preview,
  selectedKind
}: StatementImportPreviewDetailsProps) {
  if (preview.kindSummaries.length === 0) {
    return null;
  }

  const selected = preview.kindSummaries.find((summary) => summary.kind === selectedKind)
    ?? preview.kindSummaries[0]
    ?? null;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Record kinds">
        {preview.kindSummaries.map((summary) => (
          <button
            key={summary.kind}
            type="button"
            aria-pressed={summary.kind === selected?.kind}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-[2px] border px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              summary.kind === selected?.kind
                ? "border-primary bg-primary/15 text-primary"
                : "border-border bg-secondary/35 text-muted-foreground hover:border-[#ADB8C4]"
            )}
            onClick={() => onSelectKind(summary.kind)}
          >
            {summary.kind}
            <span aria-hidden="true">{summary.recordCount}</span>
            <span className="sr-only">{summary.recordCount} records</span>
          </button>
        ))}
      </div>
      {selected && selected.sampleRecords.length > 0 ? (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-xs" aria-label={`Sample ${selected.kind} records`}>
            <caption className="sr-only">Sample records parsed for the {selected.kind} kind</caption>
            <thead>
              <tr className="border-b border-border text-left font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                <th scope="col" className="px-2 py-1.5">Account</th>
                <th scope="col" className="px-2 py-1.5">Symbol</th>
                <th scope="col" className="px-2 py-1.5">Activity</th>
                <th scope="col" className="px-2 py-1.5">Trade date</th>
                <th scope="col" className="px-2 py-1.5 text-right">Quantity</th>
                <th scope="col" className="px-2 py-1.5 text-right">Price</th>
                <th scope="col" className="px-2 py-1.5 text-right">Cash</th>
              </tr>
            </thead>
            <tbody>
              {selected.sampleRecords.map((record, index) => (
                <tr key={`${record.externalTransactionId ?? record.symbol}-${index}`} className="border-b border-border/60">
                  <td className="px-2 py-1.5 font-mono">{record.account}</td>
                  <td className="px-2 py-1.5 font-mono">{record.symbol}</td>
                  <td className="px-2 py-1.5" title={record.providerActivityCode ?? undefined}>
                    {record.activitySubtype ?? record.activityType}
                  </td>
                  <td className="px-2 py-1.5 font-mono">{record.tradeDate}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.quantity}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.price}</td>
                  <td className="px-2 py-1.5 text-right font-mono">{record.cashAmount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </div>
  );
}

function StatementIssueList({ preview }: { preview: StatementImportPreview }) {
  if (preview.issues.length === 0) {
    return null;
  }

  return (
    <ul className="flex flex-col gap-1.5" aria-label="Statement import issues">
      {preview.issues.map((issue, index) => (
        <li
          key={`${issue.code}-${issue.rowNumber ?? "file"}-${index}`}
          className="flex flex-wrap items-center gap-2 rounded-[2px] border border-border/60 bg-card px-2 py-1.5 text-xs"
        >
          <Badge variant={statementIssueBadgeVariant(issue.severity)}>{issue.severity}</Badge>
          <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
            {issue.code}
            {issue.rowNumber !== null ? ` · row ${issue.rowNumber}` : ""}
            {issue.field ? ` · ${issue.field}` : ""}
          </span>
          <span className="min-w-0 flex-1">
            {issue.message}
            {issue.code === STATEMENT_FORMAT_DRIFT_ISSUE_CODE
              ? " Review the mapping profile before committing — the statement layout drifted from the accepted fingerprint."
              : ""}
          </span>
        </li>
      ))}
    </ul>
  );
}
