import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SecurityReportInputCompare } from "./security-report-input-compare";
import { getSecurityReportInputs, type SecurityReportInputCompare as CompareResponse } from "@/lib/api/security-report-inputs.api";

vi.mock("@/lib/api/security-report-inputs.api", () => ({ getSecurityReportInputs: vi.fn() }));

function response(securityId = "security-a"): CompareResponse {
  const security = { securityId, displayName: "Retained bond", version: 2, assetClass: "Bond", currency: "USD", effectiveFrom: "2026-01-01", assetSpecificTerms: { couponRate: 5.25 } };
  return {
    securityId, currentCanonical: { ...security, displayName: "Current bond", version: 3, assetSpecificTerms: { couponRate: 5.75 } },
    currentRates: [{ field: "couponRate", rawValue: "5.75", unit: "Percent (RecordDeclared)", normalizedFraction: "0.0575", reason: null }], gaps: [],
    reports: [{ runId: "october-release", reportAsOf: "2026-10-31", sourceCutoffUtc: "2026-10-31T23:59:59Z", releasedAtUtc: "2026-11-02T00:00:00Z", sourceCheckpointHash: "checkpoint",
      gaps: [], inputs: [{ journalEntryId: "journal-a", receiptSha256: "receipt-hash", security, modelVersion: "bond-context-v5", calculationContext: { KnownAt: "2026-10-10T00:00:00Z" }, source: {},
        rates: [{ field: "couponRate", rawValue: "5.25", unit: "Percent (RecordDeclared)", normalizedFraction: "0.0525", reason: null }],
        changeProvenance: "Maker and approver attribution is unavailable in this receipt." }] }]
  };
}

beforeEach(() => { vi.resetAllMocks(); vi.mocked(getSecurityReportInputs).mockResolvedValue(response()); });

describe("exact report input comparison", () => {
  it("loads on demand and keeps retained terms, current terms and time coordinates separate", async () => {
    const user = userEvent.setup();
    render(<SecurityReportInputCompare securityId="security-a" />);
    expect(getSecurityReportInputs).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Load retained report inputs" }));
    expect(await screen.findByText("5.25")).toBeInTheDocument();
    expect(screen.getByText("5.75")).toBeInTheDocument();
    expect(screen.getByText("Fraction 0.0525")).toBeInTheDocument();
    expect(screen.getByText(/Recorded timestamp eligibility cutoff/)).toHaveTextContent("2026-10-10");
    expect(screen.getByText(/Maker and approver attribution is unavailable/)).toBeInTheDocument();
    await user.click(screen.getByText("Exact terms and calculation evidence"));
    expect(screen.getByText("Receipt SHA-256 receipt-hash")).toBeInTheDocument();
  });

  it("shows legacy gaps without substituting current canonical terms for report inputs", async () => {
    const value = response();
    value.reports[0] = { ...value.reports[0], inputs: [], gaps: ["Exact inputs were not retained for this legacy report."] };
    vi.mocked(getSecurityReportInputs).mockResolvedValue(value);
    render(<SecurityReportInputCompare securityId="security-a" runId="october-release" />);
    expect(await screen.findByText("Exact input evidence incomplete")).toBeInTheDocument();
    expect(screen.getByText(/were not retained/)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(getSecurityReportInputs).toHaveBeenCalledWith("security-a", "october-release", expect.any(AbortSignal));
  });

  it("does not expose the previous security while a new request is pending", async () => {
    let resolveFirst: (value: CompareResponse) => void = () => {};
    vi.mocked(getSecurityReportInputs).mockImplementationOnce(() => new Promise(resolve => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(response("security-b"));
    const rendered = render(<SecurityReportInputCompare securityId="security-a" runId="october-release" />);
    rendered.rerender(<SecurityReportInputCompare securityId="security-b" runId="october-release" />);
    resolveFirst(response("security-a"));
    await waitFor(() => expect(getSecurityReportInputs).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("5.25")).toBeInTheDocument();
    expect(screen.queryByText("security-a")).not.toBeInTheDocument();
  });

  it("reports denied input access without showing cached data", async () => {
    vi.mocked(getSecurityReportInputs).mockRejectedValue(new Error("Report access is denied."));
    render(<SecurityReportInputCompare securityId="security-a" runId="private-run" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Report access is denied");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("shows proved exact-version makers and approval separately from the event actor", async () => {
    const value = response();
    value.reports[0].inputs[0].attribution = { securityVersion: 2, eventActor: "publisher", eventRecordedAt: "2026-10-01T00:00:00Z",
      revisionId: "revision-exact", draftedBy: "maker", submittedBy: "submitter", approvedBy: "checker", approvedAt: "2026-09-30T00:00:00Z",
      approvalId: "decision-exact", evidenceRoute: "/api/workstation/operations/continuity/workflow-exact", gaps: [] };
    vi.mocked(getSecurityReportInputs).mockResolvedValue(value);
    render(<SecurityReportInputCompare securityId="security-a" runId="october-release" />);
    expect(await screen.findByLabelText("Exact-version change attribution")).toHaveTextContent("Immutable event v2: publisher");
    expect(screen.getByText(/drafted by maker/)).toHaveTextContent("submitted by submitter");
    expect(screen.getByText(/Approved by checker/)).toHaveTextContent("decision-exact");
    expect(screen.getByRole("link", { name: "Open retained approval evidence" })).toHaveAttribute("href", "/api/workstation/operations/continuity/workflow-exact");
  });
});
