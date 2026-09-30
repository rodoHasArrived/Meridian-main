import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EvidenceAmount } from "./EvidenceAmount";
import { getJson } from "@/lib/api";
import type { EvidencePacket } from "@/types";

vi.mock("@/lib/api", () => ({ getJson: vi.fn() }));
const subject = { subjectKind: "journal-entry", subjectId: "entry-1", ledgerBookId: "book-1" };
const packet = { subject: { ...subject, label: "Journal entry Posted" }, nodes: [], warnings: [], completeness: { status: "Missing" } } as unknown as EvidencePacket;
beforeEach(() => vi.resetAllMocks());

it("shows an honest unavailable state when no retained subject exists", () => {
  render(<EvidenceAmount subject={null} value={25} currency="USD" />);
  expect(screen.getByText("$25.00")).toBeInTheDocument();
  expect(screen.getByText("Evidence unavailable")).toBeInTheDocument();
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
  expect(getJson).not.toHaveBeenCalled();
});

it("opens the exact scoped evidence with keyboard and restores focus on Escape", async () => {
  vi.mocked(getJson).mockResolvedValue(packet);
  const user = userEvent.setup();
  render(<EvidenceAmount subject={subject} value={25} currency="USD" />);
  const trigger = screen.getByRole("button", { name: "Inspect evidence for USD 25" });
  trigger.focus();
  await user.keyboard("{Enter}");
  expect(await screen.findByRole("dialog", { name: "Amount evidence" })).toBeInTheDocument();
  expect(await screen.findByText("No retained evidence is available for this subject.")).toBeInTheDocument();
  expect(getJson).toHaveBeenCalledWith(expect.stringContaining("/journal-entry/entry-1/packet?ledgerBookId=book-1"),
    expect.objectContaining({ allowDevelopmentFallback: false }));
  await user.keyboard("{Escape}");
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  await waitFor(() => expect(trigger).toHaveFocus());
});

it("keeps failed evidence reads explicit without changing the amount", async () => {
  vi.mocked(getJson).mockRejectedValue(new Error("Not found"));
  render(<EvidenceAmount subject={subject} value={25} currency="USD" />);
  await userEvent.click(screen.getByRole("button", { name: "Inspect evidence for USD 25" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Evidence is unavailable");
  expect(screen.queryByRole("link", { name: "Open full evidence" })).not.toBeInTheDocument();
});

it("refuses evidence returned for another subject", async () => {
  vi.mocked(getJson).mockResolvedValue({ ...packet, subject: { ...packet.subject, subjectId: "another-entry" } });
  render(<EvidenceAmount subject={subject} value={25} />);
  await userEvent.click(screen.getByRole("button", { name: /Inspect evidence/ }));
  expect(await screen.findByRole("alert")).toHaveTextContent("does not match");
});
