import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { PrivateCapitalLifecyclePanel } from "./private-capital-lifecycle-panel";
import { buildCapitalRows } from "./private-capital-lifecycle.view-model";
import type { privateCapitalApi } from "@/lib/api/private-capital-records.api";
import type { PrivateCapitalPreview, PrivateCapitalRecordCommitment, PrivateCapitalRecords, PrivateCapitalSnapshot } from "@/types/private-capital.types";

const evidence = { vaultId: "vault", documentId: "source", contentHash: "a".repeat(64) };
const commitment: PrivateCapitalRecordCommitment = { commitmentId: "c1", holderEntityId: "entity", vehicleSecurityId: "vehicle",
  vehicleName: "Synthetic Fund", strategy: "Private equity", vintageYear: 2024, currency: "USD", sourceReference: "statement", evidence,
  createdAt: "2026-11-01T00:00:00Z", createdBy: "operator" };
const snapshot: PrivateCapitalSnapshot = { snapshotId: "s1", commitmentId: "c1", asOfDate: "2026-09-30", navValuationDate: "2026-09-30",
  commitmentAmount: "1000000.000000000001", calledAmount: "600000", unfundedAmount: "500000", cumulativeDistributions: "200000",
  navAmount: "0", sourceReference: "statement", evidence, recordedAt: "2026-11-01T00:00:00Z", recordedBy: "operator" };
const records: PrivateCapitalRecords = { commitments: [commitment], snapshots: [snapshot], generatedAt: "2026-11-12T00:00:00Z", canManage: false };
function client(data = records): typeof privateCapitalApi {
  return { records: vi.fn().mockResolvedValue(data), choices: vi.fn().mockResolvedValue({ entities: [{ id: "entity", label: "Holder entity" }], vehicles: [{ id: "vehicle", label: "Synthetic Fund" }] }),
    register: vi.fn(), preview: vi.fn(), accept: vi.fn() };
}

function deferredFile(name: string) {
  let resolve!: (text: string) => void;
  let reject!: (reason: Error) => void;
  const result = new Promise<string>((done, fail) => { resolve = done; reject = fail; });
  const file = new File([], name, { type: "text/csv" });
  Object.defineProperty(file, "text", { value: vi.fn(() => result) });
  return { file, resolve, reject };
}

async function openCsvEntry(api: typeof privateCapitalApi) {
  render(<PrivateCapitalLifecyclePanel api={api} />);
  fireEvent.click(await screen.findByText("Register or import source observations"));
  fireEvent.change(screen.getByLabelText("Evidence vault ID"), { target: { value: "vault" } });
  fireEvent.change(screen.getByLabelText("Evidence document ID"), { target: { value: "source" } });
  fireEvent.change(screen.getByLabelText("Evidence SHA-256"), { target: { value: evidence.contentHash } });
  fireEvent.change(screen.getByLabelText("Entry method"), { target: { value: "csv" } });
  fireEvent.change(screen.getByLabelText("Review or correction rationale"), { target: { value: "Review source" } });
}

describe("private-capital lifecycle", () => {
  it("preserves exact evidence, reported zero, and independent unfunded capital", async () => {
    render(<PrivateCapitalLifecyclePanel api={client()} />);
    const table = await screen.findByRole("table", { name: "Private-capital lifecycle USD" });
    expect(table).toHaveTextContent("1,000,000.000000000001");
    expect(table).toHaveTextContent("500,000");
    expect(within(table).getByText("0")).toBeInTheDocument();
    fireEvent.click(within(table).getByRole("button", { name: "Synthetic Fund" }));
    expect(screen.getByRole("region", { name: "Selected commitment evidence" })).toHaveTextContent("NAV valuation 2026-09-30");
    expect(screen.queryByRole("button", { name: "Register commitment" })).not.toBeInTheDocument();
  });

  it("does not expose another selected client's commitments", async () => {
    const api = client();
    render(<PrivateCapitalLifecyclePanel api={api} holderEntityIds={["different-entity"]} />);
    await screen.findByText(/No retained commitment register entries/);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("keeps financial records readable without offering entry when entity write scope is denied", async () => {
    const api = client({ ...records, canManage: true });
    vi.mocked(api.choices).mockResolvedValue({ entities: [], vehicles: [] });
    render(<PrivateCapitalLifecyclePanel api={api} />);
    await screen.findByRole("table", { name: "Private-capital lifecycle USD" });
    await waitFor(() => expect(api.choices).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("Register or import source observations")).not.toBeInTheDocument();
    expect(api.preview).not.toHaveBeenCalled();
  });

  it("discards late reads when the selected scope changes", async () => {
    let resolve!: (value: PrivateCapitalRecords) => void;
    const api = client({ ...records, commitments: [], snapshots: [] });
    vi.mocked(api.records).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const { rerender } = render(<PrivateCapitalLifecyclePanel api={api} scopeKey="A" />);
    rerender(<PrivateCapitalLifecyclePanel api={api} scopeKey="B" />);
    await screen.findByText(/No retained commitment register entries/);
    resolve(records);
    await waitFor(() => expect(screen.queryByRole("table")).not.toBeInTheDocument());
  });

  it.each(["success", "failure"] as const)("retains the newer CSV file when an older file finishes with %s", async outcome => {
    const api = client({ ...records, canManage: true });
    await openCsvEntry(api);
    const older = deferredFile("older.csv");
    const newer = deferredFile("newer.csv");
    fireEvent.change(screen.getByLabelText("CSV file"), { target: { files: [older.file] } });
    fireEvent.change(screen.getByLabelText("CSV file"), { target: { files: [newer.file] } });
    await act(async () => newer.resolve("newer source CSV"));
    expect(screen.getByLabelText("CSV content")).toHaveValue("newer source CSV");

    await act(async () => {
      if (outcome === "success") older.resolve("obsolete source CSV");
      else older.reject(new Error("Obsolete file read failed"));
    });
    expect(screen.getByLabelText("CSV content")).toHaveValue("newer source CSV");
    expect(screen.queryByText("Obsolete file read failed")).not.toBeInTheDocument();
    expect(api.preview).not.toHaveBeenCalled();
  });

  it("preserves manually edited CSV instead of replacing it with a pending file", async () => {
    const api = client({ ...records, canManage: true });
    vi.mocked(api.preview).mockResolvedValue({ batchId: "manual-batch", fingerprint: "manual-fingerprint", rows: [snapshot], evidence,
      rationale: "Review source", createdAt: "2026-11-12T00:00:00Z", createdBy: "operator" });
    await openCsvEntry(api);
    const pending = deferredFile("pending.csv");
    fireEvent.change(screen.getByLabelText("CSV file"), { target: { files: [pending.file] } });
    fireEvent.change(screen.getByLabelText("CSV content"), { target: { value: "reviewed manual CSV" } });
    await act(async () => pending.resolve("obsolete file CSV"));

    expect(screen.getByLabelText("CSV content")).toHaveValue("reviewed manual CSV");
    fireEvent.submit(screen.getByRole("form", { name: "Preview private-capital observations" }));
    await screen.findByRole("region", { name: "Private-capital import preview" });
    expect(api.preview).toHaveBeenCalledWith({ rows: null, csv: "reviewed manual CSV", evidence, rationale: "Review source" });
  });

  it("retires a pending file before previewing the current draft", async () => {
    const api = client({ ...records, canManage: true });
    let resolvePreview!: (value: PrivateCapitalPreview) => void;
    vi.mocked(api.preview).mockImplementation(() => new Promise(done => { resolvePreview = done; }));
    await openCsvEntry(api);
    fireEvent.change(screen.getByLabelText("CSV content"), { target: { value: "current reviewed CSV" } });
    const pending = deferredFile("still-reading.csv");
    fireEvent.change(screen.getByLabelText("CSV file"), { target: { files: [pending.file] } });
    fireEvent.submit(screen.getByRole("form", { name: "Preview private-capital observations" }));
    await waitFor(() => expect(api.preview).toHaveBeenCalledWith({ rows: null, csv: "current reviewed CSV", evidence, rationale: "Review source" }));
    await act(async () => pending.resolve("late file CSV"));
    expect(screen.getByLabelText("CSV content")).toHaveValue("current reviewed CSV");

    await act(async () => resolvePreview({ batchId: "reviewed-batch", fingerprint: "reviewed-fingerprint", rows: [snapshot], evidence,
      rationale: "Review source", createdAt: "2026-11-12T00:00:00Z", createdBy: "operator" }));
    expect(screen.getByRole("region", { name: "Private-capital import preview" })).toHaveTextContent("reviewed-fingerprint");
    expect(screen.getByLabelText("CSV content")).toHaveValue("current reviewed CSV");
    expect(screen.getByRole("button", { name: "Accept reviewed observations" })).toBeDisabled();
    expect(api.accept).not.toHaveBeenCalled();
  });

  it("requires reviewed preview and reuses the command identity after an uncertain acceptance", async () => {
    const api = client({ ...records, canManage: true });
    vi.mocked(api.preview).mockResolvedValue({ batchId: "batch", fingerprint: "fingerprint", rows: [snapshot], evidence,
      rationale: "Review source", createdAt: "2026-11-12T00:00:00Z", createdBy: "operator" });
    vi.mocked(api.accept).mockRejectedValueOnce(new Error("Connection lost"))
      .mockRejectedValueOnce(new ApiError({ path: "/api/private-capital/accept", status: 403, detail: "Permission changed" }))
      .mockResolvedValueOnce({ commandId: "receipt",
      batchId: "batch", snapshotIds: ["s2"], acceptedAt: "2026-11-12T00:00:00Z" });
    render(<PrivateCapitalLifecyclePanel api={api} />);
    await screen.findByText("Register or import source observations");
    fireEvent.click(screen.getByText("Register or import source observations"));
    fireEvent.change(screen.getByLabelText("Evidence vault ID"), { target: { value: "vault" } });
    fireEvent.change(screen.getByLabelText("Evidence document ID"), { target: { value: "source" } });
    fireEvent.change(screen.getByLabelText("Evidence SHA-256"), { target: { value: evidence.contentHash } });
    fireEvent.submit(screen.getByRole("form", { name: "Preview private-capital observations" }));
    const accept = await screen.findByRole("button", { name: "Accept reviewed observations" });
    expect(accept).toBeDisabled();
    fireEvent.click(screen.getByLabelText("I reviewed these source amounts and correction references."));
    fireEvent.click(accept);
    await screen.findByText("The write outcome is uncertain");
    fireEvent.click(screen.getByRole("button", { name: "Retry same acceptance" }));
    await waitFor(() => expect(api.accept).toHaveBeenCalledTimes(2));
    await screen.findByText("Permission changed");
    fireEvent.click(screen.getByRole("button", { name: "Retry same acceptance" }));
    await waitFor(() => expect(api.accept).toHaveBeenCalledTimes(3));
    expect(vi.mocked(api.accept).mock.calls[0][0]).toEqual(vi.mocked(api.accept).mock.calls[1][0]);
    expect(vi.mocked(api.accept).mock.calls[0][0]).toEqual(vi.mocked(api.accept).mock.calls[2][0]);
    expect(vi.mocked(api.accept).mock.calls[0][0]).toEqual({ batchId: "batch", fingerprint: "fingerprint", commandId: expect.any(String) });
  });

  it("blocks observation acceptance until an uncertain registration is resolved", async () => {
    const api = client({ ...records, canManage: true });
    vi.mocked(api.preview).mockResolvedValue({ batchId: "batch", fingerprint: "fingerprint", rows: [snapshot], evidence,
      rationale: "Review source", createdAt: "2026-11-12T00:00:00Z", createdBy: "operator" });
    vi.mocked(api.register).mockRejectedValueOnce(new Error("Registration response lost"))
      .mockResolvedValueOnce({ ...commitment, commitmentId: "c2" });
    render(<PrivateCapitalLifecyclePanel api={api} />);
    fireEvent.click(await screen.findByText("Register or import source observations"));
    fireEvent.change(screen.getByLabelText("Evidence vault ID"), { target: { value: "vault" } });
    fireEvent.change(screen.getByLabelText("Evidence document ID"), { target: { value: "source" } });
    fireEvent.change(screen.getByLabelText("Evidence SHA-256"), { target: { value: evidence.contentHash } });
    fireEvent.change(screen.getByLabelText("Registered commitment"), { target: { value: "c1" } });
    fireEvent.change(screen.getByLabelText("Financial as-of date"), { target: { value: "2026-09-30" } });
    fireEvent.change(screen.getByLabelText("Observation source reference"), { target: { value: "statement" } });
    fireEvent.change(screen.getByLabelText("Review or correction rationale"), { target: { value: "Review source" } });
    fireEvent.submit(screen.getByRole("form", { name: "Preview private-capital observations" }));
    const accept = await screen.findByRole("button", { name: "Accept reviewed observations" });
    fireEvent.click(screen.getByLabelText("I reviewed these source amounts and correction references."));
    expect(accept).toBeEnabled();

    fireEvent.change(screen.getByLabelText("Holder entity"), { target: { value: "entity" } });
    fireEvent.change(screen.getByLabelText("Private-fund vehicle"), { target: { value: "vehicle" } });
    fireEvent.change(screen.getByLabelText("Commitment currency"), { target: { value: "USD" } });
    fireEvent.change(screen.getByLabelText("Register source reference"), { target: { value: "second commitment" } });
    fireEvent.submit(screen.getByRole("form", { name: "Register private-capital commitment" }));
    await screen.findByText("Registration response lost");
    expect(accept).toBeDisabled();
    expect(screen.getByRole("button", { name: "Retry same registration" })).toBeEnabled();
    fireEvent.click(accept);
    expect(api.accept).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Retry same registration" }));
    await screen.findByText("Commitment registered. Add its source-reported observation below.");
    expect(api.register).toHaveBeenCalledTimes(2);
    expect(vi.mocked(api.register).mock.calls[1][0]).toBe(vi.mocked(api.register).mock.calls[0][0]);
    expect(screen.queryByRole("button", { name: "Retry same registration" })).not.toBeInTheDocument();
    expect(accept).toBeEnabled();
  });

  it("does not resurrect corrected evidence for earlier financial dates and refuses conflicts", () => {
    const correction = { ...snapshot, snapshotId: "s2", supersedesSnapshotId: "s1", asOfDate: "2026-10-31" };
    expect(buildCapitalRows([commitment], [snapshot, correction], "vintage", "2026-09-30")[0].snapshot).toBeNull();
    expect(buildCapitalRows([commitment], [snapshot, correction], "vintage")[0].snapshot?.snapshotId).toBe("s2");
    const conflict = { ...snapshot, snapshotId: "conflict" };
    expect(buildCapitalRows([commitment], [snapshot, conflict], "vintage")[0]).toMatchObject({ snapshot: null, conflicting: true });
  });
});
