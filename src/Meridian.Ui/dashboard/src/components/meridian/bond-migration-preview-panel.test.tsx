import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { BondMigrationPreviewPanel } from "./bond-migration-preview-panel";

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";

describe("bond migration preview", () => {
  it("requires the current terms version and an explicit coupon unit", async () => {
    const preview = vi.fn();
    const { rerender } = render(
      <BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={null} preview={preview} />
    );
    expect(
      screen.getByRole("button", {
        name: "Preview convention migration"
      })
    ).toBeDisabled();
    rerender(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    expect(preview).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("retained coupon unit");
  });

  it("shows hash-bound prior/candidate payment comparisons without issuing a write", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [
        {
          paymentDate: "2027-01-31",
          previousPrincipal: "100",
          candidatePrincipal: "100",
          previousInterest: "12.5",
          candidateInterest: "0.125"
        }
      ],
      gaps: [],
      canStage: true
    }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await waitFor(() => expect(screen.getByText("Preview ready for governed review")).toBeInTheDocument());
    expect(preview).toHaveBeenCalledTimes(1);
    expect(preview.mock.calls[0][0]).toMatchObject({
      securityId: SECURITY_ID,
      expectedVersion: 7,
      scheduleConventions: { couponRateUnit: "Percent" }
    });
    expect(
      screen.getByRole("table", {
        name: "Bond migration payment comparison"
      })
    ).toHaveTextContent("0.125");
    expect(screen.queryByRole("button", { name: /apply|publish|post/i })).not.toBeInTheDocument();
  });

  it("stages a governed draft only after explicit justification, submitting hashes rather than amounts", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [],
      gaps: [],
      canStage: true
    }));
    const stage = vi.fn().mockResolvedValue({
      securityId: SECURITY_ID,
      revisionId: "revision-8",
      newVersion: 8,
      state: "Draft",
      changeEntry: {}
    });
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} stage={stage} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    const button = await screen.findByRole("button", {
      name: "Stage governed draft"
    });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Review justification"), {
      target: {
        value: "Contract confirmation reviewed against changed payments."
      }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Stage reviewed bond migration" }));
    expect(await screen.findByText("Governed revision created")).toBeInTheDocument();
    // Focus moves in an effect after the result renders, so wait for it rather than racing it.
    await waitFor(() =>
      expect(screen.getByRole("region", { name: "Governed bond migration result" })).toHaveFocus());
    expect(stage).toHaveBeenCalledTimes(1);
    expect(stage.mock.calls[0][0]).toMatchObject({
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      justification: "Contract confirmation reviewed against changed payments."
    });
    expect(stage.mock.calls[0][0]).not.toHaveProperty("cashDifferences");
    expect(screen.queryByRole("button", { name: "Stage governed draft" })).not.toBeInTheDocument();
    expect(screen.getByText(/No approval or publication was requested/)).toBeInTheDocument();
  });

  it("blocks reuse of a stale preview and asks for a fresh retained version", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [],
      gaps: [],
      canStage: true
    }));
    const stage = vi.fn().mockRejectedValue(new ApiError({ path: "/migration-submit", status: 409 }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} stage={stage} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), {
      target: { value: "Percent" }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await screen.findByRole("button", { name: "Stage governed draft" });
    fireEvent.change(screen.getByLabelText("Review justification"), {
      target: { value: "Reviewed." }
    });
    fireEvent.submit(screen.getByRole("form", { name: "Stage reviewed bond migration" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("prepare a fresh preview");
    expect(screen.getByRole("button", { name: "Stage governed draft" })).toBeDisabled();
  });
});

describe("bond migration preview inputs", () => {
  it("defaults settlement to the preview date and sends an explicit settlement when entered", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [],
      gaps: [],
      canStage: true
    }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.change(screen.getByLabelText("Preview date (UTC)"), { target: { value: "2026-11-02" } });
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), { target: { value: "Percent" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(1));
    expect(preview.mock.calls[0][0].context).toMatchObject({ asOf: "2026-11-02T00:00:00Z", settlementDate: "2026-11-02" });

    fireEvent.change(screen.getByLabelText("Settlement date"), { target: { value: "2026-11-04" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await waitFor(() => expect(preview).toHaveBeenCalledTimes(2));
    expect(preview.mock.calls[1][0].context).toMatchObject({ settlementDate: "2026-11-04" });
  });

  it("lists every gap separately and keeps duplicate payment dates as distinct rows", async () => {
    const preview = vi.fn().mockImplementation(async (request) => ({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      cashDifferences: [
        { paymentDate: "2027-01-31", previousPrincipal: "0", candidatePrincipal: "0", previousInterest: "12.5", candidateInterest: "0.125" },
        { paymentDate: "2027-01-31", previousPrincipal: "100", candidatePrincipal: "100", previousInterest: "0", candidateInterest: "0" }
      ],
      gaps: ["Valuation evidence is unavailable.", "Accounting evidence is unavailable."],
      canStage: false
    }));
    render(<BondMigrationPreviewPanel securityId={SECURITY_ID} expectedVersion={7} preview={preview} />);
    fireEvent.change(screen.getByLabelText("Retained coupon unit"), { target: { value: "Fraction" } });
    fireEvent.submit(screen.getByRole("form", { name: "Bond migration preview inputs" }));
    await screen.findByText("Migration has unresolved gaps");
    const gaps = screen.getAllByRole("listitem").map((item) => item.textContent);
    expect(gaps).toEqual(expect.arrayContaining(["Valuation evidence is unavailable.", "Accounting evidence is unavailable."]));
    const rows = screen.getAllByRole("row");
    expect(rows.filter((row) => row.textContent?.includes("2027-01-31"))).toHaveLength(2);
  });
});
