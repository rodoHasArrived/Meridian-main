import { act, screen, waitFor } from "@testing-library/react";
import { useNavigate } from "react-router-dom";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ApiError } from "@/lib/api-errors";
import { commandRateRepairBatch, createRateRepairBatch, discoverRateRepairs, getRateRepairBatch, listRateRepairBatches } from "@/lib/api/security-rate-repair.api";
import { SecurityRateRepairQueueRegion } from "./security-master-rate-repair-queue";
import { RateRepairSelection } from "./security-rate-repair-selection";
import { RateRepairBatchReview } from "./security-rate-repair-batch-review";
import { candidate, createInput, discovery, repairBatchId, retainedBatch } from "./security-rate-repair.test-fixtures";

vi.mock("@/lib/api/security-rate-repair.api", async importOriginal => ({
  ...await importOriginal<typeof import("@/lib/api/security-rate-repair.api")>(),
  commandRateRepairBatch: vi.fn(), createRateRepairBatch: vi.fn(), discoverRateRepairs: vi.fn(), getRateRepairBatch: vi.fn(), listRateRepairBatches: vi.fn()
}));
vi.mock("@/components/meridian/security-impact-preview", () => ({ SecurityImpactPreview: ({ receiptId, onInspected }: { receiptId: string; onInspected: (receipt: unknown) => void }) => <button onClick={() => onInspected({ contentSha256: receiptId, securityId: receiptId === "foreign-impact" ? "foreign-security" : "11111111-1111-1111-1111-111111111111", revisionId: "revision-1", canonicalVersion: 7 })}>Inspect retained impact contents</button> }));

async function completeProposal(user: ReturnType<typeof userEvent.setup>) {
  const input = createInput();
  await user.click(screen.getByRole("checkbox", { name: "Select Synthetic CD" }));
  expect(screen.getByLabelText("Rate unit")).toHaveValue("");
  await user.selectOptions(screen.getByLabelText("Rate unit"), "Percent");
  for (const [label, value] of [["Fund account ID", input.scope.fundAccountId], ["Ledger book ID", input.scope.ledgerBookId],
    ["Fund profile", input.scope.fundProfileId], ["Accounting period", input.scope.periodId], ["Retained evidence ID", input.evidenceId],
    ["Justification", input.justification]] as const) await user.type(screen.getByLabelText(label), value);
  await user.type(screen.getByLabelText("Effective date (UTC)"), "2026-11-01");
  await user.click(screen.getByRole("checkbox", { name: /I reviewed the source field/ }));
}
const otherBatchId = "99999999-9999-9999-9999-999999999999";
function BatchNavigation() {
  const navigate = useNavigate();
  return <button onClick={() => navigate("/accounting/security-master?rateRepair=queue&rateRepairBatch=" + otherBatchId)}>Open another batch</button>;
}
const otherBatch = () => {
  const batch = retainedBatch({ batchId: otherBatchId });
  batch.items[0].displayName = "Other batch CD";
  return batch;
};

describe("rate-unit repair queue", () => {
  beforeEach(() => {
    vi.clearAllMocks(); vi.mocked(discoverRateRepairs).mockResolvedValue(discovery());
    vi.mocked(listRateRepairBatches).mockResolvedValue([]); vi.mocked(getRateRepairBatch).mockResolvedValue(retainedBatch());
  });
  it("loads only on explicit opening and exposes real discovery failures", async () => {
    const user = userEvent.setup();
    vi.mocked(discoverRateRepairs).mockRejectedValue(new ApiError({ path: "/rate-repairs", status: 403, detail: "Your scope cannot inspect this queue." }));
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: ["/accounting/security-master"] });
    expect(discoverRateRepairs).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Open rate-unit repair queue" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Permission denied for this Meridian role.");
    expect(screen.queryByRole("button", { name: "Freeze repair batch" })).not.toBeInTheDocument();
  });
  it("keeps discovery grouping separate from compatible cohort selection", async () => {
    const user = userEvent.setup();
    renderWithRouter(<RateRepairSelection candidates={[candidate(), candidate({ securityId: "another", displayName: "Other schema CD", sourceSchemaVersion: "vendor-v3" })]}
      action={{ command: "Create", enabled: true, disabledReason: null }} disabled={false} onCreate={vi.fn()} />);
    await user.click(screen.getByRole("checkbox", { name: "Select Synthetic CD" }));
    expect(screen.getByRole("checkbox", { name: "Select Other schema CD" })).toBeDisabled();
    expect(screen.getByText("Different source cohort")).toBeVisible();
    expect(screen.getAllByText("0.500000000000")).toHaveLength(2);
  });
  it("freezes record versions and an evidenced unit without rewriting the scalar", async () => {
    const user = userEvent.setup(); const onCreate = vi.fn();
    renderWithRouter(<RateRepairSelection candidates={[candidate()]} action={{ command: "Create", enabled: true, disabledReason: null }} disabled={false} onCreate={onCreate} />);
    await completeProposal(user);
    await user.click(screen.getByRole("button", { name: "Freeze repair batch" }));
    expect(onCreate).toHaveBeenCalledOnce();
    expect(onCreate.mock.calls[0][0]).toEqual({ ...createInput(), requestId: expect.any(String) });
    expect(screen.getByText("0.500000000000")).toBeVisible();
    expect(onCreate.mock.calls[0][0]).not.toHaveProperty("rawValue");
  });
  it("selects a whole compatible visible cohort without admitting another schema", async () => {
    const user = userEvent.setup();
    renderWithRouter(<RateRepairSelection candidates={[candidate(), candidate({ securityId: "same-cohort", displayName: "Compatible CD" }),
      candidate({ securityId: "other-schema", displayName: "Other schema", sourceSchemaVersion: "other-version" })]}
      action={{ command: "Create", enabled: true, disabledReason: null }} disabled={false} onCreate={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Select all compatible records in this view" })).toBeDisabled();
    await user.click(screen.getByRole("checkbox", { name: "Select Synthetic CD" }));
    await user.click(screen.getByRole("button", { name: "Select all compatible records in this view" }));
    expect(screen.getByRole("checkbox", { name: "Select Compatible CD" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Select Other schema" })).not.toBeChecked();
    expect(screen.getByLabelText("Rate unit")).toHaveValue("");
    expect(screen.getByText("Freeze 2 selected records for review")).toBeVisible();
  });
  it("locks an unconfirmed create and retries its exact original request identity", async () => {
    const user = userEvent.setup();
    vi.mocked(createRateRepairBatch).mockRejectedValueOnce(new Error("Connection interrupted after dispatch.")).mockImplementationOnce(async input => retainedBatch({ batchId: input.requestId }));
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: ["/accounting/security-master?rateRepair=queue"] });
    await screen.findByRole("checkbox", { name: "Select Synthetic CD" });
    await completeProposal(user); await user.click(screen.getByRole("button", { name: "Freeze repair batch" }));
    await screen.findByText("Frozen save awaiting confirmation");
    expect(screen.getByLabelText("Recent repair batch (up to 200)")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Freeze repair batch" })).not.toBeInTheDocument();
    const original = vi.mocked(createRateRepairBatch).mock.calls[0][0];
    await user.click(screen.getByRole("button", { name: "Retry same frozen batch" }));
    await waitFor(() => expect(createRateRepairBatch).toHaveBeenCalledTimes(2));
    expect(vi.mocked(createRateRepairBatch).mock.calls[1][0]).toBe(original);
    expect(commandRateRepairBatch).not.toHaveBeenCalled();
  });
  it("requires inspection of retained impacts before approving and keeps conflicts visible", async () => {
    const user = userEvent.setup(); const onCommand = vi.fn();
    const batch = retainedBatch(); batch.items[0] = { ...batch.items[0], state: "Submitted", revisionId: "revision-1", workflowId: "workflow-1", impactReceiptId: "impact-1" };
    batch.items.push({ ...batch.items[0], securityId: "conflicting-record", displayName: "Conflicting CD", state: "Conflicted", impactReceiptId: null, errorCode: "STALE_BASE", error: "This record changed after selection." });
    batch.actions = [{ command: "Approve", enabled: true, disabledReason: null }];
    renderWithRouter(<RateRepairBatchReview batch={batch} busy={null} needsRefresh={false} onCommand={onCommand} />);
    expect(screen.getByText(/STALE_BASE: This record changed/)).toBeVisible();
    await user.type(screen.getByLabelText("Review rationale"), "Reviewed original evidence and impact.");
    await user.click(screen.getByRole("checkbox", { name: /I reviewed the frozen unit/ }));
    expect(screen.getByRole("button", { name: "Approve reviewed records" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Inspect impact for Synthetic CD" }));
    await user.click(screen.getByRole("button", { name: "Inspect retained impact contents" }));
    await user.click(screen.getByRole("button", { name: "Approve reviewed records" }));
    expect(onCommand).toHaveBeenCalledWith("approve", { expectedVersion: 1, reviewer: null, rationale: "Reviewed original evidence and impact." });
  });
  it("keeps approval blocked when a loaded impact belongs to a different security", async () => {
    const user = userEvent.setup(); const onCommand = vi.fn(); const batch = retainedBatch();
    batch.items[0] = { ...batch.items[0], state: "Submitted", revisionId: "revision-1", impactReceiptId: "foreign-impact" };
    batch.actions = [{ command: "Approve", enabled: true, disabledReason: null }];
    renderWithRouter(<RateRepairBatchReview batch={batch} busy={null} needsRefresh={false} onCommand={onCommand} />);
    await user.type(screen.getByLabelText("Review rationale"), "Reviewed.");
    await user.click(screen.getByRole("checkbox", { name: /I reviewed the frozen unit/ }));
    await user.click(screen.getByRole("button", { name: "Inspect impact for Synthetic CD" }));
    await user.click(screen.getByRole("button", { name: "Inspect retained impact contents" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("does not match this frozen security");
    expect(screen.getByRole("button", { name: "Approve reviewed records" })).toBeDisabled();
    expect(onCommand).not.toHaveBeenCalled();
  });
  it("recovers an unconfirmed create by its exact retained request ID", async () => {
    const user = userEvent.setup();
    vi.mocked(createRateRepairBatch).mockRejectedValue(new Error("Lost acknowledgement."));
    vi.mocked(getRateRepairBatch).mockImplementation(async batchId => retainedBatch({ batchId }));
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: ["/accounting/security-master?rateRepair=queue"] });
    await screen.findByRole("checkbox", { name: "Select Synthetic CD" }); await completeProposal(user);
    await user.click(screen.getByRole("button", { name: "Freeze repair batch" }));
    await user.click(await screen.findByRole("button", { name: "Load retained result" }));
    await screen.findByText("Frozen repair batch");
    await waitFor(() => expect(screen.getByLabelText("Recent repair batch (up to 200)")).toBeEnabled());
    expect(screen.queryByText("Frozen save awaiting confirmation")).not.toBeInTheDocument();
    expect(commandRateRepairBatch).not.toHaveBeenCalled();
  });
  it("uses server maker-checker refusal rather than the session display name", () => {
    const batch = retainedBatch({ actions: [{ command: "Approve", enabled: false, disabledReason: "The authenticated maker cannot approve their own proposal." }] });
    renderWithRouter(<RateRepairBatchReview batch={batch} busy={null} needsRefresh={false} onCommand={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Approve reviewed records" })).toBeDisabled();
    expect(screen.getByText("The authenticated maker cannot approve their own proposal.")).toBeVisible();
  });
  it("loads a deep-linked retained batch for explicit recovery without publishing", async () => {
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: [`/accounting/security-master?rateRepair=queue&rateRepairBatch=${repairBatchId}`] });
    await screen.findByText("Frozen repair batch");
    expect(getRateRepairBatch).toHaveBeenCalledWith(repairBatchId, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(commandRateRepairBatch).not.toHaveBeenCalled();
  });
  it("ignores an old command acknowledgement after history navigation selects another batch", async () => {
    const user = userEvent.setup(); let finish!: (batch: ReturnType<typeof retainedBatch>) => void;
    vi.mocked(commandRateRepairBatch).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    vi.mocked(getRateRepairBatch).mockImplementation(async id => id === otherBatchId ? otherBatch() : retainedBatch());
    renderWithRouter(<><BatchNavigation /><SecurityRateRepairQueueRegion /></>, { initialEntries: ["/accounting/security-master?rateRepair=queue&rateRepairBatch=" + repairBatchId] });
    await screen.findByText("Frozen repair batch");
    await user.click(screen.getByRole("button", { name: "Stage per-record drafts" }));
    await user.click(screen.getByRole("button", { name: "Open another batch" }));
    await screen.findByText("Other batch CD");
    await act(async () => finish(retainedBatch({ version: 2 })));
    expect(screen.getByLabelText("Recent repair batch (up to 200)")).toHaveValue(otherBatchId);
    expect(screen.getByText("Other batch CD")).toBeVisible();
    expect(screen.queryByText("Synthetic CD")).not.toBeInTheDocument();
    expect(vi.mocked(commandRateRepairBatch).mock.calls[0][3]?.signal?.aborted).toBe(true);
  });
  it("does not navigate back to an old create result after the operator selects another batch", async () => {
    const user = userEvent.setup(); let finish!: (batch: ReturnType<typeof retainedBatch>) => void;
    vi.mocked(createRateRepairBatch).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    vi.mocked(getRateRepairBatch).mockImplementation(async id => id === otherBatchId ? otherBatch() : retainedBatch({ batchId: id }));
    renderWithRouter(<><BatchNavigation /><SecurityRateRepairQueueRegion /></>, { initialEntries: ["/accounting/security-master?rateRepair=queue"] });
    await screen.findByRole("checkbox", { name: "Select Synthetic CD" }); await completeProposal(user);
    await user.click(screen.getByRole("button", { name: "Freeze repair batch" }));
    const requestId = vi.mocked(createRateRepairBatch).mock.calls[0][0].requestId;
    await user.click(screen.getByRole("button", { name: "Open another batch" }));
    await screen.findByText("Other batch CD");
    await act(async () => finish(retainedBatch({ batchId: requestId })));
    expect(screen.getByLabelText("Recent repair batch (up to 200)")).toHaveValue(otherBatchId);
    expect(screen.queryByText("Frozen save awaiting confirmation")).not.toBeInTheDocument();
  });
  it("loads explicit continuation pages and clears selection when an overlapping record changes", async () => {
    const user = userEvent.setup();
    vi.mocked(discoverRateRepairs).mockResolvedValueOnce({ ...discovery(), scannedCount: 200, nextSkip: 200, isComplete: false })
      .mockResolvedValueOnce({ ...discovery(), skip: 200, candidates: [candidate({ version: 8, rawValue: "50" })] });
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: ["/accounting/security-master?rateRepair=queue"] });
    await user.click(await screen.findByRole("checkbox", { name: "Select Synthetic CD" }));
    expect(screen.getByText("Freeze 1 selected records for review")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Load more source records" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("prior selection was cleared");
    expect(screen.getByRole("checkbox", { name: "Select Synthetic CD" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Select Synthetic CD" })).toBeDisabled();
    expect(screen.getByText("0.500000000000")).toBeVisible();
    expect(discoverRateRepairs).toHaveBeenLastCalledWith(expect.objectContaining({ signal: expect.any(AbortSignal) }), { skip: 200, pageSize: 200 });
  });
  it("exposes continuation when an initial source page contains no repair candidates", async () => {
    const user = userEvent.setup();
    vi.mocked(discoverRateRepairs).mockResolvedValueOnce({ ...discovery(), candidates: [], scannedCount: 200, nextSkip: 200, isComplete: false })
      .mockResolvedValueOnce({ ...discovery(), skip: 200 });
    renderWithRouter(<SecurityRateRepairQueueRegion />, { initialEntries: ["/accounting/security-master?rateRepair=queue"] });
    await screen.findByText(/More source rows are available/);
    expect(screen.queryByRole("checkbox", { name: "Select Synthetic CD" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Load more source records" }));
    expect(await screen.findByRole("checkbox", { name: "Select Synthetic CD" })).toBeVisible();
    expect(screen.getByText(/1 distinct returned candidates from 201 source rows/)).toBeVisible();
  });
});
