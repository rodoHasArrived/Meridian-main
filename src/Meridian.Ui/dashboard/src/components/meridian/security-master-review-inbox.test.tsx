import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { SecurityMasterPendingRevision } from "@/lib/api/security-master-workbench.api";
import { formatRevisionAge, SecurityMasterReviewInboxPanel } from "./security-master-review-inbox";

const submitted: SecurityMasterPendingRevision = {
  revisionId: "11111111-1111-1111-1111-111111111111",
  securityId: "22222222-2222-2222-2222-222222222222",
  state: "Submitted",
  actor: "analyst.a",
  createdAt: "2026-11-24T10:00:00Z",
  updatedAt: "2026-11-24T10:00:00Z",
  fieldPath: "assetSpecificTerms.couponRate",
  fieldValue: "4.25",
  justification: "Per term sheet.",
  fundProfileId: null,
  baseVersion: 3,
  ageHours: 30
};

describe("SecurityMasterReviewInboxPanel", () => {
  it("lists a colleague's submitted revision so an independent reviewer can open it", async () => {
    const onOpenSecurity = vi.fn();
    render(<SecurityMasterReviewInboxPanel load={() => Promise.resolve([submitted])} onOpenSecurity={onOpenSecurity} />);

    expect(await screen.findByText("assetSpecificTerms.couponRate")).toBeInTheDocument();
    expect(screen.getByText("analyst.a")).toBeInTheDocument();
    expect(screen.getByText("30h")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Open" }));
    expect(onOpenSecurity).toHaveBeenCalledWith(submitted.securityId, submitted.revisionId);
  });

  it("discards a revision and reloads the inbox", async () => {
    const load = vi.fn().mockResolvedValueOnce([submitted]).mockResolvedValueOnce([]);
    const discard = vi.fn().mockResolvedValue({});
    render(<SecurityMasterReviewInboxPanel load={load} discard={discard} />);

    await userEvent.click(await screen.findByRole("button", { name: "Discard" }));

    expect(discard).toHaveBeenCalledWith(submitted, "Discarded from the review inbox.");
    await waitFor(() => expect(screen.getByText("No revisions are awaiting action.")).toBeInTheDocument());
  });

  it("does not offer discard for an approved revision", async () => {
    render(<SecurityMasterReviewInboxPanel load={() => Promise.resolve([{ ...submitted, state: "Approved" }])} />);

    expect(await screen.findByText("Approved")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Discard" })).not.toBeInTheDocument();
  });

  it("formats waiting time compactly", () => {
    expect(formatRevisionAge(0.4)).toBe("under 1h");
    expect(formatRevisionAge(5)).toBe("5h");
    expect(formatRevisionAge(72)).toBe("3d");
  });
});
