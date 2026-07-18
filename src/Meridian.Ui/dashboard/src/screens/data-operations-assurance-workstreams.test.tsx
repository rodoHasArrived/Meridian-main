import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  IngestionOperationsWorkstream,
  StorageAssuranceWorkstream
} from "@/screens/data-operations-assurance-workstreams";

const api = vi.hoisted(() => ({
  getIngestionOperations: vi.fn(),
  applyIngestionOperationAction: vi.fn(),
  getStorageAssurance: vi.fn(),
  previewStorageMaintenance: vi.fn(),
  executeStorageMaintenance: vi.fn()
}));

vi.mock("@/lib/api/data-operations-assurance.api", () => api);

describe("data operations assurance workstreams", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getIngestionOperations.mockResolvedValue({
      generatedAt: "2026-07-13T18:00:00Z",
      summary: { total: 1, queued: 0, running: 0, paused: 0, failed: 1, completed: 0, cancelled: 0, resumable: 1 },
      providers: ["alpaca"],
      jobs: [{
        jobId: "job-1", workloadType: "Historical", state: "Failed", provider: "alpaca", symbols: ["AAPL"],
        createdAt: "2026-07-13T17:00:00Z", startedAt: null, completedAt: null, progressPercent: 42,
        isResumable: true, attemptCount: 1, maxRetries: 3, nextRetryAt: null, errorMessage: "Provider timeout",
        evidenceRoute: "/data/evidence?subjectId=job-1",
        actions: [{ action: "retry", label: "Retry", enabled: true, disabledReason: null }]
      }]
    });
    api.applyIngestionOperationAction.mockResolvedValue({ jobId: "job-1", action: "retry", previousState: "Failed", currentState: "Queued" });
    api.getStorageAssurance.mockResolvedValue({
      generatedAt: "2026-07-13T18:00:00Z",
      health: { status: "Healthy", rootLabel: "data", totalBytes: 2048, fileCount: 2, readable: true, writable: true, orphanCount: 0, temporaryFileCount: 1, message: null },
      quality: { status: "Available", filesAnalyzed: 2, averageScore: 0.98, lowQualityFileCount: 0, recommendations: [], message: null },
      canonicalization: { enabled: true, version: 2, eventsTotal: 100, successTotal: 99, softFailTotal: 1, hardFailTotal: 0, matchRatePercent: 99, providers: [] },
      capacity: { usedBytes: 2048, availableBytes: 4096, usedPercent: 33.3, estimatedDaysRemaining: null, status: "Healthy" },
      tiers: [], alerts: [], permissions: { canView: true, canRunQualityCheck: true, canMigrate: true, canDelete: true }
    });
    api.previewStorageMaintenance.mockResolvedValue({
      previewId: "preview-1", action: "Cleanup", createdAt: "2026-07-13T18:00:00Z", expiresAt: "2026-07-13T18:15:00Z",
      digest: "abc", confirmationText: "DELETE 1 FILES", affectedBytes: 128,
      candidates: [{ candidateId: "candidate-1", relativePath: "stale.tmp", kind: "Temporary", sizeBytes: 128, lastModifiedAt: "2026-07-13T17:00:00Z", fingerprint: "fingerprint" }],
      relativePath: null, targetTier: null, warnings: []
    });
    api.executeStorageMaintenance.mockResolvedValue({
      runId: "run-1", action: "Cleanup", startedAt: "2026-07-13T18:01:00Z", completedAt: "2026-07-13T18:01:01Z",
      status: "Completed", affectedBytes: 128, items: [], warnings: [], evidenceVaultId: "vault-1", evidenceRoute: "/data/evidence"
    });
  });

  it("surfaces resumability, retained evidence, and enabled job actions", async () => {
    render(<MemoryRouter><IngestionOperationsWorkstream /></MemoryRouter>);
    expect(await screen.findByText("job-1")).toBeInTheDocument();
    expect(screen.getByText("Provider timeout")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open" })).toHaveAttribute("href", "/data/evidence?subjectId=job-1");

    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    await waitFor(() => expect(api.applyIngestionOperationAction).toHaveBeenCalledWith(
      "job-1", "retry", expect.objectContaining({ rationale: "Operator-reviewed ingestion recovery" })
    ));
  });

  it("requires an unchanged typed confirmation before executing cleanup", async () => {
    render(<MemoryRouter><StorageAssuranceWorkstream /></MemoryRouter>);
    expect(await screen.findByText("Temporary candidates")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Action"), { target: { value: "Cleanup" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview action" }));
    expect(await screen.findByText("Preview: Cleanup")).toBeInTheDocument();

    const execute = screen.getByRole("button", { name: "Execute guarded action" });
    expect(execute).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Type “DELETE 1 FILES”"), { target: { value: "DELETE 1 FILES" } });
    expect(execute).toBeEnabled();
    fireEvent.click(execute);

    await waitFor(() => expect(api.executeStorageMaintenance).toHaveBeenCalledWith(expect.objectContaining({
      previewId: "preview-1", confirmationText: "DELETE 1 FILES"
    })));
    expect(await screen.findByText("Cleanup Completed")).toBeInTheDocument();
  });
});
