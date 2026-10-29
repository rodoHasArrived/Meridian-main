import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type {
  BondMigrationPreview,
  BondMigrationPreviewRequest,
  BondMigrationSubmitRequest
} from "@/types/bond-migration.types";
import type { SecurityMasterEditResult } from "./security-master-workbench.api";

/** Read-only economic preview. Publishing remains in the governed Security Master workflow. */
export function previewBondMigration(
  request: BondMigrationPreviewRequest,
  options: ApiRequestOptions = {}
): Promise<BondMigrationPreview> {
  return apiPostJson<BondMigrationPreview>(UI_API_ROUTES.ReferenceDataBondMigrationPreview, request, {
    ...options,
    allowDevelopmentFallback: false
  });
}

export function previewBondMigrationBatch(
  bonds: BondMigrationPreviewRequest[],
  options: ApiRequestOptions = {}
): Promise<BondMigrationPreview[]> {
  if (bonds.length < 1 || bonds.length > 100)
    return Promise.reject(new Error("Choose between 1 and 100 bonds for a migration preview batch."));
  return apiPostJson<BondMigrationPreview[]>(
    UI_API_ROUTES.ReferenceDataBondMigrationBatchPreview,
    { bonds },
    { ...options, allowDevelopmentFallback: false }
  );
}

export function stageBondMigration(
  request: BondMigrationSubmitRequest,
  options: ApiRequestOptions = {}
): Promise<SecurityMasterEditResult> {
  return apiPostJson<SecurityMasterEditResult>(UI_API_ROUTES.ReferenceDataBondMigrationSubmit, request, {
    ...options,
    allowDevelopmentFallback: false
  });
}
