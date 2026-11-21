import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getReportRunReview, type ReportRunReview } from "@/lib/report-run-review-api";
import { ReportReviewPanel } from "@/screens/report-review-panel";
import { renderWithRouter } from "@/test/render";

vi.mock("@/lib/report-run-review-api", () => ({ getReportRunReview: vi.fn() }));
const review: ReportRunReview = {
  reviewId: "a".repeat(64), runId: "current", version: 3, snapshotHash: "b".repeat(64),
  periodId: "may", currency: "USD", accountingBasis: "Gaap", baselineRunId: "approved", baselineVersion: 8,
  parameters: [{ key: "templateVersion", previous: "1", current: "2" }],
  sourceChanges: [{ key: "entry · amount", previous: "10", current: "10.01" }],
  lines: [{ gridId: "income", rowKey: "coupon", column: "amount", state: "Changed",
    previous: "10", current: "10.01", delta: "0.01", percent: "0.1", evidenceIds: ["snapshot:evidence"] }],
  materialityOutcome: "NotAssessed", materialChangeCount: null,
  unavailableReasons: ["Materiality not assessed: no authoritative report/template policy is configured."]
};

describe("ReportReviewPanel", () => {
  beforeEach(() => vi.resetAllMocks());
  it("renders exact retained differences and an honest materiality state", async () => {
    vi.mocked(getReportRunReview).mockResolvedValue(review);
    const onReview = vi.fn();
    const { container } = renderWithRouter(<ReportReviewPanel runId="current" version={3} onReview={onReview} />);
    expect(await screen.findByText("0.01")).toBeInTheDocument();
    expect(screen.getByText("0.1%")).toBeInTheDocument();
    expect(screen.getByText("Materiality not assessed")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Prior approved run" })).toHaveAttribute("href", "/reporting/runs/detail?runId=approved");
    expect(screen.getByText("approved", { selector: "dd" })).not.toBeVisible();
    expect(onReview).toHaveBeenLastCalledWith(review);
    expect((await axe(container)).violations).toEqual([]);
  });
  it("shows the authoritative policy version, exact thresholds and material line outcome", async () => {
    vi.mocked(getReportRunReview).mockResolvedValue({ ...review, materialityOutcome: "MaterialException", materialChangeCount: 1,
      materialityPolicy: { policyId: "approved-policy", version: "policy-v2", currency: "USD",
        metrics: [{ gridId: "income", column: "amount", absoluteVariance: "0.005", relativeVariancePercent: null }] },
      lines: review.lines.map((line) => ({ ...line, materialityOutcome: "MaterialException" })), unavailableReasons: [] });
    renderWithRouter(<ReportReviewPanel runId="current" version={3} />);
    expect(await screen.findByText("1 material line change")).toBeInTheDocument();
    expect(screen.getByLabelText("Authoritative materiality policy")).toHaveTextContent("approved-policy · version policy-v2 · USD");
    expect(screen.getByLabelText("Authoritative materiality policy")).toHaveTextContent("absolute tolerance 0.005 USD");
    expect(screen.getByRole("cell", { name: "Material exception" })).toBeInTheDocument();
  });

  it("uses the shared Amount passport and discloses a missing scoped reference", async () => {
    vi.mocked(getReportRunReview).mockResolvedValue(review);
    renderWithRouter(<ReportReviewPanel runId="current" version={3} />);
    fireEvent.click(await screen.findByRole("button", { name: "Inspect Current income / coupon / amount: 10.01 USD" }));
    const drawer = await screen.findByRole("dialog", { name: "Amount passport" }, { timeout: 5000 });
    expect(within(drawer).getByText("An amount-scoped passport reference was not retained for this report comparison. Run and snapshot evidence is available.")).toBeInTheDocument();
  });

  it("rejects mismatched versions and allows a retry without inferred data", async () => {
    vi.mocked(getReportRunReview).mockResolvedValueOnce({ ...review, version: 2 }).mockResolvedValueOnce(review);
    const onReview = vi.fn();
    renderWithRouter(<ReportReviewPanel runId="current" version={3} onReview={onReview} />);
    expect(await screen.findByText("Review evidence unavailable")).toBeInTheDocument();
    expect(onReview).not.toHaveBeenCalledWith(expect.objectContaining({ reviewId: review.reviewId }));
    fireEvent.click(screen.getByRole("button", { name: "Retry review evidence" }));
    await waitFor(() => expect(onReview).toHaveBeenLastCalledWith(review));
  });
});
