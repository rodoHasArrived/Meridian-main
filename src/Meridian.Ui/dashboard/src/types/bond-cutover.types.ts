/** Explicit reviewed block bound to the retained bond identity, version and content hash it was reviewed against. */
export interface BondMigrationBlock {
  securityId: string;
  version: number;
  securityContentSha256: string;
  reason: string;
}

export interface BondMigrationInventoryRow {
  securityId: string;
  version: number;
  securityContentSha256: string;
  displayName: string;
  hasDeclaredConventions: boolean;
  /** Declaration blocker for the current retained terms; null when the bond declares supported conventions. */
  blockedReason: string | null;
}

/** Full active Security Master inventory read; not a transaction-visibility certificate. */
export interface BondMigrationInventory {
  inventorySha256: string;
  bonds: BondMigrationInventoryRow[];
  cutoverEnabled: boolean;
  requiresLegacyCalculations: boolean;
  currentReceiptSha256: string | null;
}

/**
 * The server replaces `reviewer` with the authenticated actor. `expectedPreviousReceiptSha256` must
 * name the current receipt when amending an enabled cutover and must be absent for the first cutover.
 */
export interface BondMigrationCutoverRequest {
  expectedInventorySha256: string;
  reviewer: string;
  explicitBlocks: BondMigrationBlock[];
  expectedPreviousReceiptSha256?: string | null;
}

export interface BondMigrationCutoverReceipt {
  inventorySha256: string;
  reviewer: string;
  recordedAt: string;
  explicitBlocks: BondMigrationBlock[];
  modelVersion: string;
  previousReceiptSha256: string | null;
}
