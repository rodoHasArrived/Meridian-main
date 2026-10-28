import type { EvidencePacket } from "@/types";
import type { LedgerAmountProof, LedgerAmountProofEvidence } from "@/types/ledger-amount-proof";

const sha256 = /^[a-fA-F0-9]{64}$/;
const guid = /^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$/;
const guardKeys = ["ledgerAmountSubjectId", "ledgerBookId", "periodId", "fundProfileId", "expectedContentHash"];

export function isCurrentRetainedEvidence(item: LedgerAmountProofEvidence): boolean {
  const retainedAt = item.retainedAt ? Date.parse(item.retainedAt) : NaN;
  return item.status === "Ready" && Number.isFinite(retainedAt) && retainedAt <= Date.now();
}

/** Match the server's scoped, digest-bound vault route and canonical artifact identity. */
export function hasExactRetainedSource(packet: EvidencePacket, proof: LedgerAmountProof, item: LedgerAmountProofEvidence): boolean {
  if (typeof item.route !== "string" || !item.route.startsWith("/workstation/evidence/vault/") ||
    !item.contentHash || !sha256.test(item.contentHash) || !Array.isArray(packet.nodes)) return false;
  let url: URL;
  try { url = new URL(item.route, "https://meridian.invalid"); } catch { return false; }
  if (url.origin !== "https://meridian.invalid" ||
    !/^\/workstation\/evidence\/vault\/[a-zA-Z0-9_-]+$/.test(url.pathname) || url.hash) return false;
  const keys = [...url.searchParams.keys()];
  if (keys.length !== guardKeys.length || guardKeys.some((key) => url.searchParams.getAll(key).length !== 1)) return false;
  if (url.searchParams.get("ledgerAmountSubjectId") !== proof.subjectId ||
    url.searchParams.get("ledgerBookId") !== proof.scope.ledgerBookId ||
    url.searchParams.get("periodId") !== proof.scope.periodId ||
    url.searchParams.get("fundProfileId") !== proof.scope.fundProfileId ||
    url.searchParams.get("expectedContentHash")?.toLowerCase() !== item.contentHash.toLowerCase()) return false;

  const reportAmount = proof.subjectId.startsWith("report:");
  const sourceScope = reportAmount ? item.sourceScope : proof.scope;
  const sourceSubjectId = reportAmount ? item.sourceSubjectId : proof.subjectId;
  if (!sourceScope || !sourceSubjectId) return false;
  if (reportAmount) {
    const parts = sourceSubjectId.split(":");
    if (sourceScope.tenantId !== proof.scope.tenantId || sourceScope.companyId !== proof.scope.companyId ||
      sourceScope.fundProfileId !== proof.scope.fundProfileId || sourceScope.ledgerBookId !== proof.scope.ledgerBookId ||
      !guid.test(sourceScope.periodId) || parts.length !== 3 || !guid.test(parts[0]!) || !guid.test(parts[1]!) ||
      (parts[2] !== "debit" && parts[2] !== "credit")) return false;
  }
  // A report guard addresses the report; the source artifact retains its original posting period.
  // Uri.EscapeDataString also escapes these characters which encodeURIComponent leaves intact.
  const fund = encodeURIComponent(sourceScope.fundProfileId).replace(/[!'()*]/g, (value) => `%${value.charCodeAt(0).toString(16).toUpperCase()}`);
  const canonicalSubject = `${fund}:${sourceScope.ledgerBookId}:${sourceScope.periodId}:${sourceSubjectId}`;
  const nodes = packet.nodes.filter((node) => node?.evidenceId === item.evidenceId);
  if (nodes.length !== 1 || nodes[0]?.subject?.subjectId !== sourceSubjectId || nodes[0]?.subject?.subjectKind !== "ledger-amount" ||
    !Array.isArray(nodes[0]?.artifactRefs)) return false;
  const artifacts = nodes[0].artifactRefs.filter((artifact) => artifact?.artifactId === item.evidenceId);
  if (artifacts.length !== 1) return false;
  const matchingArtifacts = artifacts
    .filter((artifact) => artifact.artifactId === item.evidenceId && artifact.kind === item.kind && artifact.retained &&
      artifact.route === item.route && artifact.canonicalSubjectKind === "ledger-amount" &&
      artifact.canonicalSubjectId === canonicalSubject && artifact.hash !== null && sha256.test(artifact.hash) &&
      artifact.hash.toLowerCase() === item.contentHash!.toLowerCase() && Date.parse(artifact.generatedAt) === Date.parse(item.retainedAt!));
  return matchingArtifacts.length === 1;
}
