/**
 * Read-only audit-chain integrity report served by `GET /api/ledger/audit-integrity`.
 * Mirrors `Meridian.Contracts.Ledger.AccountingAuditIntegrityReportDto` and
 * `AuditChainIntegrityResultDto` (positional members only; parity-checked).
 */

/** Stable chain identifiers (`AuditChainIdentifiers`). */
export type AuditChainIdentifier = "accounting-actions" | "ledger-events";

/** Stable status values (`AuditChainIntegrityStatus`). */
export type AuditChainIntegrityStatus = "verified" | "broken" | "not-configured" | "unavailable";

export interface AuditChainIntegrityResultDto {
  chain: string;
  status: string;
  linksChecked: number;
  unprotectedPreChainCount: number;
  failureKind?: string | null;
  detail?: string | null;
  failedSequence?: number | null;
}

export interface AccountingAuditIntegrityReportDto {
  verifiedAtUtc: string;
  allVerified: boolean;
  chains: AuditChainIntegrityResultDto[];
}
