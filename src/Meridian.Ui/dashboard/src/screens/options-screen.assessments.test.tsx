import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { OptionAssessmentPanel } from "./options-screen.assessments";
import { comparableBasket, EMPTY_OPTION_BASKET, emptyAssessment } from "./options-screen.view-model";
import type { OptionAssessmentImportPreview, RetainedOptionAssessment } from "@/types/option-visualization.types";
import { ApiError } from "@/lib/api-errors";
const api = vi.hoisted(() => ({ list: vi.fn(), preview: vi.fn(), accept: vi.fn() }));
vi.mock("@/lib/api/option-visualization.api", () => ({ listOptionAssessments: api.list, previewOptionAssessment: api.preview,
  acceptOptionAssessment: api.accept, optionAssessmentSourceUrl: () => "/source" }));
const account = "f3550de4-a0d8-459d-9c8a-cb2eb3eb6f98";
const assessment = { ...emptyAssessment(account, EMPTY_OPTION_BASKET), providerId: "synthetic", externalAccountId: "account",
  assessedAt: "2026-11-12T12:00:00Z", brokerReference: "report-1", outcome: "Approved" as const, rawBrokerStatus: "Approved on report" };
const preview: OptionAssessmentImportPreview = { assessment, basketFingerprint: "basket-hash", previewFingerprint: "preview-hash",
  sourceSha256: "source-hash", sourceBytes: 12, correctsAssessmentId: null, warnings: ["No current acceptance."] };
const retained: RetainedOptionAssessment = { assessmentId: "record-a", assessment, basketFingerprint: "basket-hash", contentSha256: "content-hash",
  recordedAt: "2026-11-12T12:05:00Z", recordedBy: "operator", sourceVaultId: "source", sourceDocumentId: "document", sourceSha256: "source-hash",
  assessmentVaultId: "assessment", assessmentDocumentId: "assessment-doc", correctsAssessmentId: null, isSuperseded: false };
function mount() { return renderWithRouter(<OptionAssessmentPanel account={account} basket={EMPTY_OPTION_BASKET}
  basketKey={comparableBasket(EMPTY_OPTION_BASKET)} onUseBasket={vi.fn()} />); }

describe("option broker assessment evidence", () => {
  beforeEach(() => { vi.clearAllMocks(); api.list.mockResolvedValue([]); api.preview.mockResolvedValue(preview); api.accept.mockResolvedValue(retained); });
  it("keeps history errors explicit and never substitutes sample assessments", async () => {
    api.list.mockRejectedValue(new Error("retained source failed verification"));
    const user = userEvent.setup(); mount(); await user.click(screen.getByRole("button", { name: "Load assessment history" }));
    await screen.findByRole("alert"); expect(screen.queryByText(/matching imported assessment\(s\)/)).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Retained broker assessment history" })).not.toBeInTheDocument();
  });
  it("requires a source and explicit preview, then retries uncertain acceptance with the exact command", async () => {
    api.accept.mockRejectedValueOnce(new Error("network outcome unknown")).mockResolvedValueOnce(retained);
    const user = userEvent.setup(); mount();
    expect(screen.getByRole("button", { name: "Preview evidence import" })).toBeDisabled();
    await user.upload(screen.getByLabelText(/Original broker source document/), new File(["broker source"], "source.txt", { type: "text/plain" }));
    await screen.findByText("source.txt attached");
    await user.click(screen.getByRole("button", { name: "Preview evidence import" }));
    await screen.findByRole("region", { name: "Reviewed broker assessment preview" });
    expect(api.accept).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Accept reviewed evidence import" }));
    await screen.findByRole("button", { name: "Retry exact import" });
    const first = api.accept.mock.calls[0][1];
    await user.click(screen.getByRole("button", { name: "Retry exact import" }));
    await waitFor(() => expect(api.accept).toHaveBeenCalledTimes(2));
    expect(api.accept.mock.calls[1][1]).toEqual(first);
    expect(first.import.sourceContentBase64).toBe(btoa("broker source"));
    await screen.findByText(/Retained broker assessment record-a/);
  });
  it("reports dated permission and restrictions separately from the imported outcome", async () => {
    api.list.mockResolvedValue([{ ...retained, assessment: { ...assessment, permissionEvidence: "Options level as reported", restrictionEvidence: "Account restricted for new positions" } }]);
    const user = userEvent.setup(); mount(); await user.click(screen.getByRole("button", { name: "Load assessment history" }));
    await screen.findByText("Approved as of 2026-11-12T12:00:00Z");
    expect(screen.getByText(/Permission evidence: Options level as reported/)).toBeInTheDocument();
    expect(screen.getByText(/Restrictions \/ collateral evidence: Account restricted/)).toBeInTheDocument();
    expect(screen.getByText(/A historical match establishes no present broker acceptance/)).toBeInTheDocument();
  });
  it("preserves an uncertain import across a forbidden retry until the same command succeeds", async () => {
    api.accept.mockRejectedValueOnce(new Error("response lost after dispatch"))
      .mockRejectedValueOnce(new ApiError({ path: "/accept", status: 403, detail: "The entity grant is unavailable." }))
      .mockResolvedValueOnce(retained);
    const user = userEvent.setup(); mount();
    await user.upload(screen.getByLabelText(/Original broker source document/), new File(["broker source"], "source.txt", { type: "text/plain" }));
    await screen.findByText("source.txt attached");
    await user.click(screen.getByRole("button", { name: "Preview evidence import" }));
    await screen.findByRole("region", { name: "Reviewed broker assessment preview" });
    await user.click(screen.getByRole("button", { name: "Accept reviewed evidence import" }));
    await screen.findByRole("button", { name: "Retry exact import" });
    const original = api.accept.mock.calls[0][1];

    await user.click(screen.getByRole("button", { name: "Retry exact import" }));
    await screen.findByText("Permission denied for this Meridian role.");
    expect(screen.getByRole("button", { name: "Retry exact import" })).toBeEnabled();
    expect(screen.getByLabelText("Broker provider ID")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview evidence import" })).toBeDisabled();
    expect(api.accept.mock.calls[1][1]).toBe(original);

    await user.click(screen.getByRole("button", { name: "Retry exact import" }));
    await screen.findByText(/Retained broker assessment record-a/);
    expect(api.accept).toHaveBeenCalledTimes(3);
    expect(api.accept.mock.calls[2][1]).toBe(original);
    expect(api.preview).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText("Broker provider ID")).toBeEnabled();
    expect(screen.queryByRole("button", { name: "Retry exact import" })).not.toBeInTheDocument();
  });
  it("distinguishes a definitive validation rejection from an uncertain transport outcome", async () => {
    api.accept.mockRejectedValue(new ApiError({ path: "/accept", status: 400, detail: "Reported currency is required." }));
    const user = userEvent.setup(); mount();
    await user.upload(screen.getByLabelText(/Original broker source document/), new File(["broker source"], "source.txt", { type: "text/plain" }));
    await screen.findByText("source.txt attached");
    await user.click(screen.getByRole("button", { name: "Preview evidence import" }));
    await screen.findByRole("region", { name: "Reviewed broker assessment preview" });
    await user.click(screen.getByRole("button", { name: "Accept reviewed evidence import" }));
    await screen.findByText("Reported currency is required.");
    expect(screen.queryByRole("button", { name: "Retry exact import" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Broker provider ID")).toBeEnabled();
  });
});
