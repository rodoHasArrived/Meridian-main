export interface PrivateCapitalEvidence { vaultId: string; documentId: string; contentHash: string }
export interface PrivateCapitalRecordCommitment {
  commitmentId: string; holderEntityId: string; vehicleSecurityId: string; vehicleName: string;
  strategy: string | null; vintageYear: number | null; currency: string; sourceReference: string;
  evidence: PrivateCapitalEvidence; createdAt: string; createdBy: string;
}
export interface PrivateCapitalSnapshotInput {
  commitmentId: string; asOfDate: string; navValuationDate: string | null;
  commitmentAmount: string | null; calledAmount: string | null; unfundedAmount: string | null;
  cumulativeDistributions: string | null; navAmount: string | null; sourceReference: string;
  supersedesSnapshotId?: string | null;
}
export interface PrivateCapitalSnapshot extends PrivateCapitalSnapshotInput {
  snapshotId: string; evidence: PrivateCapitalEvidence; recordedAt: string; recordedBy: string;
  batchId?: string;
}
export interface PrivateCapitalRecords {
  commitments: PrivateCapitalRecordCommitment[]; snapshots: PrivateCapitalSnapshot[]; generatedAt: string; canManage: boolean;
}
export interface PrivateCapitalChoices { entities: { id: string; label: string }[]; vehicles: { id: string; label: string }[] }
export interface PrivateCapitalPreview {
  batchId: string; fingerprint: string; rows: PrivateCapitalSnapshotInput[];
  evidence: PrivateCapitalEvidence; rationale: string; createdAt: string; createdBy: string;
}
export interface PrivateCapitalReceipt { commandId: string; batchId: string; snapshotIds: string[]; acceptedAt: string }
