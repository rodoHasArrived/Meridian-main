/**
 * Presentation logic for the audit-chain integrity check.
 *
 * The server reports each chain as verified, broken, not-configured, or
 * unavailable, and those are four different answers: a chain this deployment
 * does not keep is not evidence of integrity, and a store that could not be
 * read is not evidence of tampering. The view keeps them apart and never rolls
 * an unchecked chain into a passing verdict.
 */

import type { AccountingAuditIntegrityReportDto, AuditChainIntegrityResultDto } from "@/types";

export type AuditIntegrityTone = "success" | "danger" | "warning" | "default";

export interface AuditChainRow {
  chain: string;
  label: string;
  statusLabel: string;
  tone: AuditIntegrityTone;
  coverage: string;
  failure: string | null;
}

export interface AuditIntegrityViewModel {
  tone: AuditIntegrityTone;
  title: string;
  verdict: string;
  verifiedAtUtc: string;
  rows: AuditChainRow[];
}

const CHAIN_LABELS: Record<string, string> = {
  "accounting-actions": "Accounting actions",
  "ledger-events": "Ledger events"
};

const STATUS_PRESENTATION: Record<string, { label: string; tone: AuditIntegrityTone }> = {
  verified: { label: "Verified", tone: "success" },
  broken: { label: "Broken", tone: "danger" },
  unavailable: { label: "Not checked — store unavailable", tone: "warning" },
  "not-configured": { label: "Not kept in this deployment", tone: "default" }
};

export function buildAuditIntegrityViewModel(report: AccountingAuditIntegrityReportDto): AuditIntegrityViewModel {
  const rows = report.chains.map(buildRow);
  const broken = rows.filter((row) => row.tone === "danger");
  const unavailable = rows.filter((row) => row.tone === "warning");
  const configured = report.chains.filter((chain) => chain.status !== "not-configured");

  if (broken.length > 0) {
    return {
      tone: "danger",
      title: "Audit chain integrity failure",
      verdict: `${broken.map((row) => row.label).join(" and ")} did not verify. Retained history may have been altered, truncated, or interrupted mid-append; investigate before relying on it.`,
      verifiedAtUtc: report.verifiedAtUtc,
      rows
    };
  }

  if (unavailable.length > 0) {
    return {
      tone: "warning",
      title: "Audit chains partly checked",
      verdict: `${unavailable.map((row) => row.label).join(" and ")} could not be read, so nothing is claimed about ${unavailable.length === 1 ? "it" : "them"}.`,
      verifiedAtUtc: report.verifiedAtUtc,
      rows
    };
  }

  if (configured.length === 0 || !report.allVerified) {
    return {
      tone: "warning",
      title: "No audit chain verified",
      verdict: "This deployment keeps no hash-chained audit log that could be checked.",
      verifiedAtUtc: report.verifiedAtUtc,
      rows
    };
  }

  return {
    tone: "success",
    title: "Audit chains verified",
    verdict: `Every configured audit chain verified (${configured.length} of ${report.chains.length}).`,
    verifiedAtUtc: report.verifiedAtUtc,
    rows
  };
}

function buildRow(result: AuditChainIntegrityResultDto): AuditChainRow {
  const presentation = STATUS_PRESENTATION[result.status] ?? { label: result.status, tone: "warning" as const };
  return {
    chain: result.chain,
    label: CHAIN_LABELS[result.chain] ?? result.chain,
    statusLabel: presentation.label,
    tone: presentation.tone,
    coverage: describeCoverage(result),
    failure: describeFailure(result)
  };
}

function describeCoverage(result: AuditChainIntegrityResultDto): string {
  if (result.status === "not-configured" || result.status === "unavailable") {
    return result.detail ?? "—";
  }

  const links = `${result.linksChecked.toLocaleString("en-US")} chained ${result.linksChecked === 1 ? "entry" : "entries"} checked`;
  return result.unprotectedPreChainCount > 0
    ? `${links}; ${result.unprotectedPreChainCount.toLocaleString("en-US")} earlier ${result.unprotectedPreChainCount === 1 ? "record predates" : "records predate"} chaining and are not covered`
    : links;
}

function describeFailure(result: AuditChainIntegrityResultDto): string | null {
  if (result.status !== "broken") {
    return null;
  }

  const kind = result.failureKind ?? "Unclassified";
  const position = result.failedSequence != null ? ` at sequence ${result.failedSequence}` : "";
  return `${kind}${position}${result.detail ? ` — ${result.detail}` : ""}`;
}
