import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { BondReferenceLadderPanel } from "./bond-reference-ladder-panel";
import type { BondReferenceRow } from "@/types/bond-reference.types";

const row = (id: string, name: string, maturity: string): BondReferenceRow => ({
  securityId: id, displayName: name, currency: "USD", issuerName: "ACME", seniority: null, primaryIdentifier: `ISIN-${id}`,
  lifecycle: { securityId: id, issueDate: null, callDate: null, maturityDate: maturity, isCallable: false, version: 1 }, version: 1
});

describe("BondReferenceLadderPanel", () => {
  it("pages an issuer ladder by cursor and appends without duplicates", async () => {
    const issuerPage = vi.fn()
      .mockResolvedValueOnce({ items: [row("a", "ACME 5 2030", "2030-01-31"), row("b", "ACME 4 2031", "2031-01-31")], nextCursor: "cursor-1" })
      .mockResolvedValueOnce({ items: [row("b", "ACME 4 2031", "2031-01-31"), row("c", "ACME 3 2032", "2032-01-31")], nextCursor: null });
    const onSelect = vi.fn();
    render(<BondReferenceLadderPanel onSelectSecurity={onSelect} api={{ issuerPage }} />);

    expect(screen.getByRole("button", { name: "Load more" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Issuer name"), { target: { value: "ACME" } });
    fireEvent.click(screen.getByRole("button", { name: "Load ladder" }));
    await screen.findByText("ACME 5 2030");
    expect(issuerPage).toHaveBeenLastCalledWith({ issuerName: "ACME", pageSize: 100, cursor: null }, expect.anything());
    expect(screen.getByRole("status")).toHaveTextContent("2 rows loaded; more pages available.");

    fireEvent.click(screen.getByRole("button", { name: "Load more" }));
    await screen.findByText("ACME 3 2032");
    expect(issuerPage).toHaveBeenLastCalledWith({ issuerName: "ACME", pageSize: 100, cursor: "cursor-1" }, expect.anything());
    expect(screen.getAllByText(/^ACME \d/)).toHaveLength(3);
    expect(screen.getByRole("status")).toHaveTextContent("3 rows loaded; end of ladder.");
    expect(screen.getByRole("button", { name: "Load more" })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "ACME 3 2032" }));
    expect(onSelect).toHaveBeenCalledWith("c");
  });

  it("requires a maturity window, discards rows when the query changes and surfaces refusals", async () => {
    const maturityPage = vi.fn().mockResolvedValueOnce({ items: [row("m", "MUNI 2 2029", "2029-06-30")], nextCursor: null })
      .mockRejectedValueOnce(new Error("Cursor does not belong to this query."));
    render(<BondReferenceLadderPanel api={{ maturityPage }} />);

    fireEvent.change(screen.getByLabelText("Ladder kind"), { target: { value: "maturity" } });
    fireEvent.click(screen.getByRole("button", { name: "Load ladder" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Enter the maturity window start and end dates.");
    expect(maturityPage).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Maturity window start"), { target: { value: "2029-01-01" } });
    fireEvent.change(screen.getByLabelText("Maturity window end"), { target: { value: "2029-12-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Load ladder" }));
    await screen.findByText("MUNI 2 2029");
    expect(maturityPage).toHaveBeenLastCalledWith({ from: "2029-01-01", to: "2029-12-31", pageSize: 100, cursor: null }, expect.anything());

    fireEvent.change(screen.getByLabelText("Maturity window end"), { target: { value: "2030-12-31" } });
    expect(screen.queryByText("MUNI 2 2029")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Load ladder" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Cursor does not belong to this query."));
  });
});
