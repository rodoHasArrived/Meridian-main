import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import type { SecurityMasterHeldAmendment } from "@/lib/api/security-master-review-queues.api";
import { SecurityMasterHeldAmendmentsPanel } from "./security-master-held-amendments";

const held: SecurityMasterHeldAmendment = {
  heldAmendmentId: "b1111111-1111-1111-1111-111111111111",
  securityId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  sourceSystem: "vendor-b",
  requestJson: "{}",
  dominatedFieldPaths: ["EconomicDefinition.Coupon", "Identifiers.Isin"],
  conflictIds: ["c1", "c2"],
  heldAtVersion: 7,
  heldAt: "2026-11-24T09:30:00Z",
  status: "Held"
};

describe("SecurityMasterHeldAmendmentsPanel", () => {
  it("lists held rows as served and opens the security to resolve its conflict", async () => {
    const onOpenSecurity = vi.fn();
    const load = vi.fn().mockResolvedValue([held]);
    render(<SecurityMasterHeldAmendmentsPanel load={load} onOpenSecurity={onOpenSecurity} />);

    expect(await screen.findByText("vendor-b")).toBeInTheDocument();
    expect(load).toHaveBeenCalledWith("Held");
    expect(screen.getByText("EconomicDefinition.Coupon, Identifiers.Isin")).toBeInTheDocument();
    expect(screen.getByText("2026-11-24 09:30 UTC")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Open security" }));
    expect(onOpenSecurity).toHaveBeenCalledWith(held.securityId);
  });

  it("discards a held row and reloads", async () => {
    const load = vi.fn().mockResolvedValueOnce([held]).mockResolvedValueOnce([]);
    const discard = vi.fn().mockResolvedValue(undefined);
    render(<SecurityMasterHeldAmendmentsPanel load={load} discard={discard} />);

    await userEvent.click(await screen.findByRole("button", { name: "Discard" }));

    expect(discard).toHaveBeenCalledWith(held);
    expect(await screen.findByText("Discarded the held amendment from vendor-b.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("No held amendments.")).toBeInTheDocument());
  });

  it("shows the server's reason when the row is no longer held", async () => {
    const load = vi.fn().mockResolvedValue([held]);
    const discard = vi
      .fn()
      .mockRejectedValue(new ApiError({ path: "/x", status: 409, responseBody: `{"error":"Held amendment '${held.heldAmendmentId}' is no longer held."}` }));
    render(<SecurityMasterHeldAmendmentsPanel load={load} discard={discard} />);

    await userEvent.click(await screen.findByRole("button", { name: "Discard" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(`Held amendment '${held.heldAmendmentId}' is no longer held.`);
  });

  it("filters by status and offers discard only for held rows", async () => {
    const load = vi.fn().mockImplementation((status: string) => Promise.resolve(status === "Held" ? [] : [{ ...held, status }]));
    render(<SecurityMasterHeldAmendmentsPanel load={load} />);

    expect(await screen.findByText("No held amendments.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("tab", { name: "Discarded" }));

    expect(await screen.findByText("vendor-b")).toBeInTheDocument();
    expect(load).toHaveBeenLastCalledWith("Discarded");
    expect(screen.queryByRole("button", { name: "Discard" })).not.toBeInTheDocument();
  });
});
