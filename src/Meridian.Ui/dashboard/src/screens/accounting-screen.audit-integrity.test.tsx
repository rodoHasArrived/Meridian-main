import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as integrityApi from "@/lib/api/audit-integrity.api";
import { ApiError } from "@/lib/api-errors";
import { AuditChainIntegrityPanel } from "@/screens/accounting-screen.audit-integrity";

vi.mock("@/lib/api/audit-integrity.api", () => ({
  getAuditIntegrityReport: vi.fn()
}));

const api = vi.mocked(integrityApi);

afterEach(() => {
  vi.resetAllMocks();
});

describe("AuditChainIntegrityPanel", () => {
  it("does nothing until asked, because verification walks every chain", () => {
    render(<AuditChainIntegrityPanel />);

    expect(api.getAuditIntegrityReport).not.toHaveBeenCalled();
  });

  it("shows each chain's verdict after verification", async () => {
    api.getAuditIntegrityReport.mockResolvedValue({
      verifiedAtUtc: "2026-10-18T09:00:00Z",
      allVerified: false,
      chains: [
        { chain: "accounting-actions", status: "broken", linksChecked: 2, unprotectedPreChainCount: 0, failureKind: "BrokenLink", failedSequence: 2, detail: null },
        { chain: "ledger-events", status: "verified", linksChecked: 40, unprotectedPreChainCount: 0, failureKind: null, failedSequence: null, detail: null }
      ]
    });
    render(<AuditChainIntegrityPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ }));

    expect(await screen.findByText("Audit chain integrity failure")).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: "Accounting actions" })).toBeInTheDocument();
    expect(screen.getByText("BrokenLink at sequence 2")).toBeInTheDocument();
    expect(screen.getByText("40 chained entries checked")).toBeInTheDocument();
  });

  it("surfaces a failed request without inventing a verdict", async () => {
    api.getAuditIntegrityReport.mockRejectedValue(new Error("Network unreachable"));
    render(<AuditChainIntegrityPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Network unreachable");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("explains a permission refusal instead of reporting a failure", async () => {
    api.getAuditIntegrityReport.mockRejectedValue(new ApiError({ path: "/api/ledger/audit-integrity", status: 403 }));
    render(<AuditChainIntegrityPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ }));

    expect(await screen.findByText("Requires maintenance authority")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("withdraws the previous verdict while a fresh verification runs", async () => {
    api.getAuditIntegrityReport.mockResolvedValueOnce({
      verifiedAtUtc: "2026-10-18T09:00:00Z",
      allVerified: true,
      chains: [{ chain: "accounting-actions", status: "verified", linksChecked: 4, unprotectedPreChainCount: 0, failureKind: null, failedSequence: null, detail: null }]
    });
    api.getAuditIntegrityReport.mockReturnValueOnce(new Promise(() => {}));
    render(<AuditChainIntegrityPanel />);
    const button = screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ });

    await userEvent.click(button);
    expect(await screen.findByText("Audit chains verified")).toBeInTheDocument();
    await userEvent.click(button);

    expect(screen.queryByText("Audit chains verified")).not.toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("aborts a running verification when the panel unmounts", async () => {
    let signal: AbortSignal | undefined;
    api.getAuditIntegrityReport.mockImplementation((options) => {
      signal = options?.signal ?? undefined;
      return new Promise(() => {});
    });
    const { unmount } = render(<AuditChainIntegrityPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ }));
    expect(signal?.aborted).toBe(false);
    unmount();

    expect(signal?.aborted).toBe(true);
  });

  it("reports a concurrent run as busy rather than failed", async () => {
    api.getAuditIntegrityReport.mockRejectedValue(new ApiError({ path: "/api/ledger/audit-integrity", status: 429 }));
    render(<AuditChainIntegrityPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Verify the accounting and ledger audit chains/ }));

    expect(await screen.findByText("Verification already running")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
