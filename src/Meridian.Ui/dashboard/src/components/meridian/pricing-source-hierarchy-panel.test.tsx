import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { PricingSourceHierarchyPanel } from "./pricing-source-hierarchy-panel";
import type { PricingSourceHierarchy } from "@/lib/api/pricing-hierarchy.api";

const api = vi.hoisted(() => ({ load: vi.fn(), save: vi.fn() }));
vi.mock("@/lib/api/pricing-hierarchy.api", () => ({ getPricingHierarchy: api.load, savePricingHierarchy: api.save }));
const securityId = "11111111-1111-1111-1111-111111111111";
const hierarchy: PricingSourceHierarchy = {
  securityId, accountId: "account-a", asOf: "2026-10-01T00:00:00Z", updatedBy: "maintainer",
  entries: [{ sourceId: "vendor-a", sourceDisplayName: "Vendor A", priority: 1, maxDaysStale: 1 }]
};
const props = { accountId: "account-a", canRead: true, canModify: true, initialSecurityId: securityId };
const load = () => fireEvent.click(screen.getByRole("button", { name: "Load source hierarchy" }));
function effective() { fireEvent.change(screen.getByLabelText("New hierarchy effective at (UTC)"), { target: { value: "2026-11-01T12:30:00" } }); }
beforeEach(() => {
  vi.clearAllMocks(); api.load.mockResolvedValue(hierarchy); api.save.mockResolvedValue(undefined);
});

describe("Pricing source hierarchy journey", () => {
  it("loads retained policy, configures and reorders multiple sources, saves exact scope and reloads", async () => {
    const dirty = vi.fn();
    render(<PricingSourceHierarchyPanel {...props} onDirtyChange={dirty} />); load();
    expect(await screen.findByText(/Loaded version effective/)).toHaveTextContent("maintainer");
    fireEvent.click(screen.getByRole("button", { name: "Add pricing source" }));
    fireEvent.change(screen.getByLabelText("Source ID 2"), { target: { value: " vendor-b " } });
    fireEvent.change(screen.getByLabelText("Source name 2"), { target: { value: "Vendor B" } });
    fireEvent.change(screen.getByLabelText("Maximum days stale 2"), { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Move source 2 up" }));
    expect(screen.getByLabelText("Source ID 1")).toHaveValue(" vendor-b ");
    effective();
    fireEvent.click(screen.getByRole("button", { name: "Save source hierarchy version" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledOnce());
    expect(api.save.mock.calls[0][0]).toEqual({ securityId, accountId: "account-a", asOf: "2026-11-01T12:30:00.000Z", entries: [
      { priority: 1, sourceId: "vendor-b", sourceDisplayName: "Vendor B", maxDaysStale: 3 },
      { priority: 2, sourceId: "vendor-a", sourceDisplayName: "Vendor A", maxDaysStale: 1 }
    ] });
    expect(api.save.mock.calls[0][0]).not.toHaveProperty("updatedBy");
    expect(await screen.findByRole("status")).toHaveTextContent("Existing retained price sets keep their original evidence");
    expect(dirty).toHaveBeenLastCalledWith(false);
    api.load.mockResolvedValue({ ...hierarchy, entries: api.save.mock.calls[0][0].entries, asOf: "2026-11-01T12:30:00.000Z" });
    load(); expect(await screen.findByLabelText("Source ID 1")).toHaveValue("vendor-b");
  });

  it("offers first-version creation after a missing hierarchy and validates duplicates and staleness", async () => {
    api.load.mockRejectedValue(new ApiError({ path: "/hierarchy", status: 404, title: "Missing" }));
    render(<PricingSourceHierarchyPanel {...props} />); load();
    expect(await screen.findByRole("status")).toHaveTextContent("No current source hierarchy");
    fireEvent.click(screen.getByRole("button", { name: "Add pricing source" }));
    fireEvent.change(screen.getByLabelText("Source ID 1"), { target: { value: "a" } });
    fireEvent.change(screen.getByLabelText("Source name 1"), { target: { value: "A" } });
    fireEvent.change(screen.getByLabelText("Maximum days stale 1"), { target: { value: "-1" } });
    effective();
    expect(screen.getByRole("button", { name: "Save source hierarchy version" })).toBeDisabled();
    expect(screen.getByText(/Maximum staleness must/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Maximum days stale 1"), { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Add pricing source" }));
    fireEvent.change(screen.getByLabelText("Source ID 2"), { target: { value: "A" } });
    fireEvent.change(screen.getByLabelText("Source name 2"), { target: { value: "B" } });
    expect(screen.getByText(/Source IDs must be distinct/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Remove source 2" }));
    fireEvent.click(screen.getByRole("button", { name: "Save source hierarchy version" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledOnce());
  });

  it("makes read-only authority and unavailable services explicit", async () => {
    const { rerender } = render(<PricingSourceHierarchyPanel {...props} canModify={false} />); load();
    await screen.findByLabelText("Source ID 1");
    expect(screen.getByLabelText("Source ID 1")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save source hierarchy version" })).toBeDisabled();
    expect(screen.getByText(/Read only · source maintenance/)).toBeInTheDocument();
    rerender(<PricingSourceHierarchyPanel {...props} canRead={false} />);
    expect(screen.queryByRole("button", { name: "Load source hierarchy" })).not.toBeInTheDocument();
    expect(screen.getByText("Source hierarchy access unavailable")).toBeInTheDocument();
    rerender(<PricingSourceHierarchyPanel {...props} />);
    api.load.mockRejectedValue(new ApiError({ path: "/hierarchy", status: 503, title: "Pricing storage unavailable" })); load();
    expect(await screen.findByRole("alert")).toHaveTextContent("Pricing storage unavailable");
    expect(screen.queryByRole("button", { name: "Save source hierarchy version" })).not.toBeInTheDocument();
    expect(api.save).not.toHaveBeenCalled();
  });

  it("locks an uncertain write and retries only the exact reviewed timestamp and entries", async () => {
    api.save.mockRejectedValueOnce(new TypeError("Network failed"));
    render(<PricingSourceHierarchyPanel {...props} />); load();
    await screen.findByLabelText("Source ID 1"); effective();
    fireEvent.click(screen.getByRole("button", { name: "Save source hierarchy version" }));
    await screen.findByRole("button", { name: "Retry unchanged hierarchy version" });
    expect(screen.getByLabelText("Source ID 1")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Load source hierarchy" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Retry unchanged hierarchy version" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(2));
    expect(api.save.mock.calls[1][0]).toBe(api.save.mock.calls[0][0]);
    expect(await screen.findByRole("status")).toHaveTextContent("Source hierarchy version saved");
  });

  it("discards delayed data when the account or permissions change and exposes accessible fields", async () => {
    let resolve!: (value: PricingSourceHierarchy) => void;
    api.load.mockReturnValueOnce(new Promise<PricingSourceHierarchy>((done) => { resolve = done; }));
    const { container, rerender } = render(<PricingSourceHierarchyPanel {...props} />); load();
    const signal = api.load.mock.calls[0][2].signal;
    rerender(<PricingSourceHierarchyPanel {...props} accountId="account-b" />);
    expect(signal.aborted).toBe(true);
    await act(async () => resolve(hierarchy));
    expect(screen.queryByText(/Loaded version effective/)).not.toBeInTheDocument();
    api.load.mockResolvedValue({ ...hierarchy, accountId: "account-b" }); load();
    await screen.findByLabelText("Source ID 1");
    expect((await axe(container)).violations).toEqual([]);
  });
});
