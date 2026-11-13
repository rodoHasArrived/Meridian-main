import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { CorporateActionCalendar, corporateCalendarEvents } from "./corporate-action-calendar";
import { buildCorporateActionInboxModel } from "./data-screen.corporate-action-inbox.view-model";
import type { CorporateActionElectionCalendar } from "@/types";

function rows(electionCalendar?: CorporateActionElectionCalendar) {
  return buildCorporateActionInboxModel({ lastIngestAt: null, stagedCount: 1, appliedLastRun: 0, duplicatesSkippedLastRun: 0, errors: [], cases: [], staged: [{
    proposalId: "proposal-a", securityId: "security-a", ticker: "ABC", actionType: "TenderOffer", exDate: "2026-11-10", recordDate: "2026-11-10", payableDate: "2026-11-13",
    amount: null, currency: "USD", splitFromFactor: null, splitToFactor: null, winningSource: "provider", agreeingSources: ["provider"], dissentingSources: [], autoApplied: false, electionCalendar
  }] }).rows;
}

it("keeps corporate date roles distinct and places the actual offset cutoff on its UTC day", async () => {
  const data = rows({ evidenceState: "Available", messages: [], cutoffs: [{ id: "custodian", label: "Custodian instruction", deadlineAt: "2026-11-10T23:30:00-05:00", source: "Custodian notice", evidenceReference: "document://notice/v2" }] });
  const onSelect = vi.fn();
  const { container, rerender } = render(<CorporateActionCalendar rows={data} selectedRowId={null} onSelect={onSelect} />);
  fireEvent.change(screen.getByLabelText("Week starting"), { target: { value: "2026-11-09" } });
  expect(screen.getByRole("button", { name: /Ex-date for ABC, 2026-11-10/ })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /Record date for ABC, 2026-11-10/ })).toBeInTheDocument();
  const cutoff = screen.getByRole("button", { name: /Election cutoff for ABC, 2026-11-11/ });
  await userEvent.click(cutoff);
  expect(onSelect).toHaveBeenCalledWith("proposal-a");
  rerender(<CorporateActionCalendar rows={data} selectedRowId="proposal-a" onSelect={onSelect} />);
  expect(screen.getByText(/2026-11-10T23:30:00-05:00/)).toHaveTextContent("document://notice/v2");
  expect(cutoff).toHaveAttribute("aria-pressed", "true");
  expect((await axe(container)).violations).toEqual([]);
});

it("never promotes an ex-date or invalid/missing election projection into a cutoff", () => {
  const missing = rows();
  expect(corporateCalendarEvents(missing).map((event) => event.kind)).toEqual(["Ex-date", "Record date", "Payment date"]);
  const invalid = rows({ evidenceState: "InvalidEvidence", messages: ["Source cutoff cannot be verified"], cutoffs: [{ id: "bad", label: "Bad", deadlineAt: "2026-11-10T10:00:00Z", source: "source", evidenceReference: "document://bad" }] });
  render(<CorporateActionCalendar rows={invalid} selectedRowId={null} onSelect={vi.fn()} />);
  expect(screen.queryByRole("button", { name: /Election cutoff for/ })).not.toBeInTheDocument();
  expect(screen.getByText(/Actual election cutoff evidence unavailable for 1 cases/)).toBeInTheDocument();
  expect(screen.getByText(/Source cutoff cannot be verified/)).toBeInTheDocument();
});

it("retains row identity across events and changes weeks through ordinary keyboard-operable controls", async () => {
  render(<CorporateActionCalendar rows={rows()} selectedRowId={null} onSelect={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("Week starting"), { target: { value: "2026-11-09" } });
  await userEvent.click(screen.getByRole("button", { name: "Next week" }));
  expect(screen.getByLabelText("Week starting")).toHaveValue("2026-11-16");
  const table = screen.getByRole("table");
  expect(within(table).queryByRole("button")).not.toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("0 events in this week · 3 outside this week");
});
