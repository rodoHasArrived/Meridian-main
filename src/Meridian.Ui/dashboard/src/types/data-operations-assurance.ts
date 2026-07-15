export interface IngestionOperationsSnapshot {
  generatedAt: string;
  summary: { total: number; queued: number; running: number; paused: number; failed: number; completed: number; cancelled: number; resumable: number };
  jobs: IngestionOperationRow[];
  providers: string[];
}

export interface IngestionOperationRow {
  jobId: string;
  workloadType: string;
  state: string;
  provider: string;
  symbols: string[];
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  progressPercent: number;
  isResumable: boolean;
  attemptCount: number;
  maxRetries: number;
  nextRetryAt: string | null;
  errorMessage: string | null;
  evidenceRoute: string | null;
  actions: Array<{ action: string; label: string; enabled: boolean; disabledReason: string | null }>;
}

export interface IngestionOperationActionResult {
  jobId: string;
  action: string;
  previousState: string;
  currentState: string;
  recordedAt: string;
  evidenceVaultId: string | null;
  evidenceRoute: string | null;
}

export type StorageMaintenanceAction = "QualityCheck" | "Cleanup" | "TierMigration";

export interface StorageAssuranceSnapshot {
  generatedAt: string;
  health: { status: string; rootLabel: string; totalBytes: number; fileCount: number; readable: boolean; writable: boolean; orphanCount: number; temporaryFileCount: number; message: string | null };
  quality: { status: string; filesAnalyzed: number; averageScore: number; lowQualityFileCount: number; recommendations: string[]; message: string | null };
  canonicalization: { enabled: boolean; version: number; eventsTotal: number; successTotal: number; softFailTotal: number; hardFailTotal: number; matchRatePercent: number; providers: Array<{ provider: string; total: number; success: number; softFail: number; hardFail: number; matchRatePercent: number }> };
  capacity: { usedBytes: number; availableBytes: number; usedPercent: number; estimatedDaysRemaining: number | null; status: string };
  tiers: Array<{ tier: string; fileCount: number; totalBytes: number }>;
  alerts: Array<{ alertId: string; severity: string; subject: string; message: string; detectedAt: string }>;
  permissions: { canView: boolean; canRunQualityCheck: boolean; canMigrate: boolean; canDelete: boolean };
}

export interface StorageMaintenancePreview {
  previewId: string;
  action: StorageMaintenanceAction;
  createdAt: string;
  expiresAt: string;
  digest: string;
  confirmationText: string;
  affectedBytes: number;
  candidates: Array<{ candidateId: string; relativePath: string; kind: string; sizeBytes: number; lastModifiedAt: string; fingerprint: string }>;
  relativePath: string | null;
  targetTier: string | null;
  warnings: string[];
}

export interface StorageMaintenanceResult {
  runId: string;
  action: StorageMaintenanceAction;
  startedAt: string;
  completedAt: string;
  status: string;
  affectedBytes: number;
  items: Array<{ candidateId: string; relativePath: string; status: string; message: string | null }>;
  warnings: string[];
  evidenceVaultId: string | null;
  evidenceRoute: string | null;
}
