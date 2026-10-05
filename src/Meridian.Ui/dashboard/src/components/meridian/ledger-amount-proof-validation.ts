import type { EvidencePacket } from "@/types";
import type { LedgerAmountProof, LedgerAmountProofEvidence } from "@/types/ledger-amount-proof";

const sha256 = /^[a-fA-F0-9]{64}$/;
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

  // Uri.EscapeDataString also escapes these characters which encodeURIComponent leaves intact.
  const fund = encodeURIComponent(proof.scope.fundProfileId).replace(/[!'()*]/g, (value) => `%${value.charCodeAt(0).toString(16).toUpperCase()}`);
  const canonicalSubject = `${fund}:${proof.scope.ledgerBookId}:${proof.scope.periodId}:${proof.subjectId}`;
  const nodes = packet.nodes.filter((node) => node?.evidenceId === item.evidenceId);
  if (nodes.length !== 1 || nodes[0]?.subject?.subjectId !== proof.subjectId || nodes[0]?.subject?.subjectKind !== "ledger-amount" ||
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
