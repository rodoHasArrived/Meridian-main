import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type {
  BondMigrationCutoverReceipt,
  BondMigrationCutoverRequest,
  BondMigrationInventory
} from "@/types/bond-cutover.types";

/** Complete active-bond inventory with its hash and the retained cutover state. AdminMaintenance only. */
export function getBondCutoverReadiness(options: ApiRequestOptions = {}): Promise<BondMigrationInventory> {
  return apiGetJson<BondMigrationInventory>(UI_API_ROUTES.ReferenceDataBondCutoverReadiness, {
    ...options,
    allowDevelopmentFallback: false
  });
}

/**
 * Enables, or amends, the durable live-calculation gate. The server verifies the inventory hash, every
 * block's identity/version/hash, and the previous receipt hash; it records the authenticated actor as
 * reviewer regardless of the body.
 */
export function enableBondCalculationCutover(
  request: BondMigrationCutoverRequest,
  options: ApiRequestOptions = {}
): Promise<BondMigrationCutoverReceipt> {
  return apiPostJson<BondMigrationCutoverReceipt>(UI_API_ROUTES.ReferenceDataBondCutover, request, {
    ...options,
    allowDevelopmentFallback: false
  });
}
