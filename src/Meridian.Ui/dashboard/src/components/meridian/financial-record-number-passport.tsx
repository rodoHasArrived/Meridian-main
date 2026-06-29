import { NumberPassportPanel } from "@/components/meridian/number-passport";
import { financialRecordToneTextClass, financialRecordToneToBadge } from "@/lib/financial-record-explorer-tone";
import { buildNumberPassportItems } from "@/lib/financial-record-passport";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

export interface FinancialRecordNumberPassportProps {
  record: FinancialRecordExplorerSelectedRecordDto;
  row: FinancialRecordExplorerRowDto | null;
  explorer: FinancialRecordExplorerDto | null;
  title?: string;
}

export function FinancialRecordNumberPassport({
  record,
  row,
  explorer,
  title = "Number Passport"
}: FinancialRecordNumberPassportProps) {
  const items = buildNumberPassportItems(record, row, explorer);
  const statusLabel = row?.status || record.recordType;

  return (
    <NumberPassportPanel
      title={title}
      ariaLabel={`${record.title} ${title}`}
      summary={`${title} for ${record.title}: ${items.map((item) => `${item.label} ${item.value}`).join("; ")}.`}
      statusLabel={statusLabel}
      statusBadgeVariant={financialRecordToneToBadge(record.tone)}
      items={items.map((item) => ({
        id: item.id,
        label: item.label,
        value: item.value,
        detail: item.detail,
        href: item.href || undefined,
        valueClassName: financialRecordToneTextClass(item.tone)
      }))}
    />
  );
}
