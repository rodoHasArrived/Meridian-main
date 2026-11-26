import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { getSecurityImpactPreview, type SecurityImpactPreviewReceipt } from "@/lib/api/security-impact-preview.api";
import { SecurityImpactPreview } from "./security-impact-preview";
vi.mock("@/lib/api/security-impact-preview.api", () => ({ getSecurityImpactPreview: vi.fn() }));
describe("impact evidence inspection", () => {
  it("shows unavailable prior calculation and incomplete coverage without a zero delta", async () => {
    const receipt: SecurityImpactPreviewReceipt = {
      contentSha256: "a".repeat(64), dependencyFingerprint: "b".repeat(64), capturedAt: "2026-11-14T00:00:00Z",
      asOf: "2026-11-01T00:00:00Z", securityId: "security", revisionId: "revision", canonicalVersion: 3,
      books: { completeness: "Partial", bookIds: ["book"], basis: "All fund books", issues: [] },
      periods: [{ bookId: "book", periodId: "period", version: 4, startDate: "2026-10-01", endDate: "2026-10-31", status: "HardClosed" }],
      reports: { completeness: "Unavailable", runIds: [], issues: [] },
      before: { modelVersion: "v5", projection: { schedule: [], blockedReason: "Missing rate unit" } },
      after: { modelVersion: "v5", projection: { schedule: [{ amount: 5 }] } }, issues: []
    };
    vi.mocked(getSecurityImpactPreview).mockResolvedValue(receipt);
    const inspected = vi.fn();
    render(<SecurityImpactPreview receiptId={receipt.contentSha256} onInspected={inspected} />);
    expect(await screen.findByText("Missing rate unit")).toBeInTheDocument();
    expect(screen.getByText("HardClosed")).toBeInTheDocument();
    expect(screen.getByText(/zero-impact conclusion is unavailable/)).toBeInTheDocument();
    await waitFor(() => expect(inspected).toHaveBeenCalledWith(receipt));
  });
});
