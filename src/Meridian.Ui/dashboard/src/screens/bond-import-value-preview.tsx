import { bondDecimalText } from "@/lib/bond-decimal-format";

type MappedRow = { mappedRecord?: unknown; stagingRecordId?: string; quarantineRecordId?: string };
type ValueEvidence = Record<string, string | null>;

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
  const rows = records.flatMap((record, index) => {
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
      <p className="mt-1 text-[11px] text-muted-foreground">Provider value and normalized consideration on the declared market-value basis. Variance is provider minus normalized; tolerance is 0.01 in the stated currency. Dry-run previews show the first 20 mapped rows.</p>
      <div className="mt-2 overflow-x-auto">
        <table className="w-full text-left text-xs">
          <caption className="sr-only">Retained provider values compared with calculations using explicit bond units</caption>
          <thead><tr className="border-b border-border/60">
            <th scope="col" className="p-2">Holding / basis</th>
            <th scope="col" className="p-2 text-right">Provider value</th>
            <th scope="col" className="p-2 text-right">Normalized calculation</th>
            <th scope="col" className="p-2 text-right">Variance</th>
            <th scope="col" className="p-2">Evidence</th>
          </tr></thead>
          <tbody>{rows.map(({ evidence, key, label }) => (
            <tr key={key} className="border-b border-border/40 align-top">
              <th scope="row" className="p-2 font-normal"><span className="font-mono">{label}</span><div className="text-muted-foreground">{evidence.marketValueBasis ?? "Basis unavailable"} · {evidence.currency ?? "Currency unavailable"}</div></th>
              <td className="p-2 text-right font-mono">{bondDecimalText(evidence.providerValue)}</td>
              <td className="p-2 text-right font-mono">{bondDecimalText(evidence.normalizedValue)}</td>
              <td className="p-2 text-right font-mono">{bondDecimalText(evidence.variance)}</td>
              <td className="min-w-56 max-w-md p-2">
                <span>{evidence.status}</span>
                <details className="mt-1"><summary className="cursor-pointer text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">Review conventions</summary>
                  <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-[11px]">
                    <dt>Original face</dt><dd className="font-mono">{bondDecimalText(evidence.originalFace)}</dd>
                    <dt>Current factor</dt><dd className="font-mono">{bondDecimalText(evidence.currentFactor)}</dd>
                    <dt>Provider quotation</dt><dd className="font-mono">{bondDecimalText(evidence.quotation)} · {evidence.quotationConvention ?? "Unavailable"}</dd>
                    <dt>Quotation basis</dt><dd>{evidence.priceBasis ?? "Unavailable"}</dd>
                    <dt>Accrued interest (total)</dt><dd className="font-mono">{bondDecimalText(evidence.accruedInterest)}</dd>
                    <dt>Clean consideration</dt><dd className="font-mono">{bondDecimalText(evidence.cleanConsideration)}</dd>
                    <dt>Dirty consideration</dt><dd className="font-mono">{bondDecimalText(evidence.dirtyConsideration)}</dd>
                  </dl>
                  <p className="mt-2 text-[11px] text-muted-foreground">{evidence.explanation}</p>
                </details>
              </td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    </section>
  );
}
