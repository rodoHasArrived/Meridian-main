import { apiPostJson } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface UnitRegisterPreview {
  scope: { ledgerBookId: string };
  periodId: string;
  asOf: string;
  shareClassId: string;
  currency: string;
  unitsOutstanding: string;
  classNav: string;
  navPerUnit: string;
  highWaterNavPerUnit: string;
  holdings: {
    investorId: string;
    units: string;
    subscribedAmount: string;
    redeemedAmount: string;
    equalisationCredit: string;
    contingentRedemption: string;
  }[];
  movements: {
    transactionId: string;
    investorId: string;
    dealingDate: string;
    type: string;
    navPerUnit: string;
    amount: string;
    units: string;
    equalisationCredit: string;
    contingentRedemption: string;
  }[];
  issues: { code: string; message: string }[];
  evidenceLink: string;
  documentId: string;
  sourceHashSha256: string;
  closeSnapshotHashSha256: string;
  isTied: boolean;
}

export interface UnitRegisterPreviewRequest {
  periodId: string;
  evidenceLink: string;
  documentId: string;
}

export async function previewUnitRegister(ledgerBookId: string, request: UnitRegisterPreviewRequest, signal?: AbortSignal) {
  const query = new URLSearchParams({ ledgerBookId });
  const result = await apiPostJson<UnitRegisterPreview>(
    `${UI_API_ROUTES.LedgerPrivateCapitalUnitRegisterPreview}?${query}`,
    request,
    { signal, allowDevelopmentFallback: false }
  );
  if (result?.scope?.ledgerBookId?.toLowerCase() !== ledgerBookId.toLowerCase() ||
    result.periodId.toLowerCase() !== request.periodId.toLowerCase() ||
    result.documentId !== request.documentId || result.evidenceLink !== request.evidenceLink) {
    throw new Error("The unit-register preview does not match the selected ledger and evidence.");
  }
  return result;
}
