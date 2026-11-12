import type {
  AttestationRevocation,
  AttestationScope,
  PreparerReceipt,
  PrincipalDeclaration
} from "@/lib/single-principal-attestation-api";

/** Counts evidence tasks only; none of these are amount approvals or Verified Coverage. */
export interface PreparerAttestationEvidenceProjection {
  scope: AttestationScope;
  schemaVersion: number;
  availability: string;
  assessedAt: string;
  disclosure: string;
  boundary: string;
  recordedReceiptCount: number | null;
  unrevokedReceiptCount: number | null;
  revokedReceiptCount: number | null;
  syntheticReceiptCount: number | null;
  unclassifiedReceiptCount: number | null;
  acknowledgments: PreparerEvidenceAcknowledgment[];
  warnings: string[];
}

export interface PreparerEvidenceAcknowledgment {
  receipt: PreparerReceipt;
  originalDeclaration: PrincipalDeclaration;
  revocation: AttestationRevocation | null;
  applicability: string;
}
