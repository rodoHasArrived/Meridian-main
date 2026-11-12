import { bondDecimalText } from "@/lib/bond-decimal-format";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

type MappedRow = { mappedRecord?: unknown; stagingRecordId?: string; quarantineRecordId?: string };
type ValueEvidence = Record<string, string | null>;
type ValuePreviewRow = { evidence: ValueEvidence; key: string; label: string };

const columns: DenseDataTableColumn<ValuePreviewRow>[] = [
  {
    id: "holding", label: "Holding / basis", rowHeader: true, className: "align-top",
    render: ({ label, evidence }) => <><span className="font-mono">{label}</span><div className="text-muted-foreground">{evidence.marketValueBasis ?? "Basis unavailable"} · {evidence.currency ?? "Currency unavailable"}</div></>
  },
  {
    id: "provider", label: "Provider value", numeric: true, className: "align-top",
    render: ({ evidence }) => <span className="font-mono">{bondDecimalText(evidence.providerValue)}</span>
  },
  {
    id: "normalized", label: "Normalized calculation", numeric: true, className: "align-top",
    render: ({ evidence }) => <span className="font-mono">{bondDecimalText(evidence.normalizedValue)}</span>
  },
  {
    id: "variance", label: "Variance", numeric: true, className: "align-top",
    render: ({ evidence }) => <span className="font-mono">{bondDecimalText(evidence.variance)}</span>
  },
  {
    id: "evidence", label: "Evidence", className: "min-w-56 max-w-md whitespace-normal align-top",
    render: ({ evidence }) => <>
      <span>{evidence.status}</span>
      <details className="mt-1"><summary className="cursor-pointer text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">Review conventions</summary>
        <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
          <dt>Original face</dt><dd className="font-mono">{bondDecimalText(evidence.originalFace)}</dd>
          <dt>Current factor</dt><dd className="font-mono">{bondDecimalText(evidence.currentFactor)}</dd>
          <dt>Provider quotation</dt><dd className="font-mono">{bondDecimalText(evidence.quotation)} · {evidence.quotationConvention ?? "Unavailable"}</dd>
          <dt>Quotation basis</dt><dd>{evidence.priceBasis ?? "Unavailable"}</dd>
          <dt>Accrued interest (total)</dt><dd className="font-mono">{bondDecimalText(evidence.accruedInterest)}</dd>
          <dt>Clean consideration</dt><dd className="font-mono">{bondDecimalText(evidence.cleanConsideration)}</dd>
          <dt>Dirty consideration</dt><dd className="font-mono">{bondDecimalText(evidence.dirtyConsideration)}</dd>
        </dl>
        <p className="mt-2 text-xs text-muted-foreground">{evidence.explanation}</p>
      </details>
    </>
  }
];

function object(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function evidenceFor(value: unknown): ValueEvidence | null {
  const evidence = object(object(value)?.bondValueReconciliation);
  if (evidence?.schema !== "bond-value-reconciliation-v1") return null;
  // Exact evidence crosses the boundary as decimal strings; never coerce it to a JS number.
  return Object.fromEntries(Object.entries(evidence).map(([key, item]) => [key, typeof item === "string" ? item : null]));
}

export function BondImportValuePreview({
  mappedRecords = [], stagedRecords = [], quarantinedRecords = []
}: {
  mappedRecords?: readonly unknown[];
  stagedRecords?: readonly MappedRow[];
  quarantinedRecords?: readonly MappedRow[];
}) {
  const records: MappedRow[] = [
    ...mappedRecords.map((mappedRecord) => ({ mappedRecord })), ...stagedRecords, ...quarantinedRecords
  ];
  const rows: ValuePreviewRow[] = records.flatMap((record, index) => {
    const evidence = evidenceFor(record.mappedRecord);
    if (!evidence) return [];
    const mapped = object(record.mappedRecord);
    const security = object(mapped?.security);
    const identifier = security?.cusip ?? security?.isin ?? mapped?.sourceRecordId;
    return [{ evidence, key: record.stagingRecordId ?? record.quarantineRecordId ?? `preview-${index}`,
      label: typeof identifier === "string" ? identifier : `Row ${index + 1}` }];
  });
  if (rows.length === 0) return null;

  return (
    <section className="mt-3 rounded-md border border-border/70 p-3" aria-label="Bond holding value reconciliation">
      <h6 className="text-xs font-semibold text-foreground">Bond holding value reconciliation</h6>
      <p className="mt-1 text-xs text-muted-foreground">Provider value and normalized consideration on the declared market-value basis. Variance is provider minus normalized; tolerance is 0.01 in the stated currency. Dry-run previews show the first 20 mapped rows.</p>
      <div className="mt-2">
        <DenseDataTable
          rows={rows}
          columns={columns}
          getRowId={(row) => row.key}
          ariaLabel="Bond holding provider value and normalized consideration"
          caption="Retained provider values compared with calculations using explicit bond units"
          emptyText="No bond holding value evidence."
        />
      </div>
    </section>
  );
}
