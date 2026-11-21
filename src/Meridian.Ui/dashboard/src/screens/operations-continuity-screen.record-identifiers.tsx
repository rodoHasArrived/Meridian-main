import { TechnicalDetails } from "@/components/ui/technical-details";

export function CloseRecordIdentifiers({ recordId, fundAccountId }: { recordId: string; fundAccountId?: string }) {
  return <TechnicalDetails label="Record identifiers" className="mt-2" contentClassName="py-2">
    <dl className="grid gap-1 text-xs">
      <div><dt>Record ID</dt><dd className="break-all font-mono">{recordId}</dd></div>
      {fundAccountId ? <div><dt>Fund account</dt><dd className="break-all font-mono">{fundAccountId}</dd></div> : null}
    </dl>
  </TechnicalDetails>;
}
