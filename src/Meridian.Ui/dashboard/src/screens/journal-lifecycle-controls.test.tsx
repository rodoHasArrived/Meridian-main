import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { JournalLifecycleControls } from "./journal-lifecycle-controls";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";

function makeView() {
  return {
    draft: { journalEntryId: "journal-1", version: 4, entityId: "entity-1", ledgerBookId: "book-1", periodId: "period-1", currency: "USD" },
    balanceImpactRows: [{ id: "cash", accountName: "Cash", debitLabel: "$500.00", creditLabel: "$0.00", netEffectLabel: "+$500.00" }],
    lifecycleCommands: [
      { action: "Post", label: "Post", busy: false, disabledReason: null },
      { action: "Reverse", label: "Reverse", busy: false, disabledReason: null },
      { action: "Rebook", label: "Rebook", busy: false, disabledReason: null },
      { action: "LockAfterClose", label: "Lock after close", busy: false, disabledReason: null }
    ],
    applyLifecycleAction: vi.fn().mockResolvedValue(undefined)
  } as unknown as ManualJournalEntryWorkbenchViewModel;
}

it("reviews scope and impact, cancels without a command, and restores focus", async () => {
  const view = makeView();
  const user = userEvent.setup();
  render(<JournalLifecycleControls view={view} />);
  const trigger = screen.getByRole("button", { name: "Post" });
  await user.click(trigger);
  const dialog = screen.getByRole("dialog", { name: "Confirm post" });
  expect(dialog).toHaveTextContent("book-1");
  expect(dialog).toHaveTextContent("period-1");
  expect(dialog).toHaveTextContent("$500.00");
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
  await user.keyboard("{Escape}");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
});

it.each(["Reverse", "Lock after close"])("requires retained rationale for %s", async (label) => {
  const view = makeView();
  const user = userEvent.setup();
  render(<JournalLifecycleControls view={view} />);
  await user.click(screen.getByRole("button", { name: label }));
  const dialog = screen.getByRole("dialog");
  const confirm = within(dialog).getByRole("button", { name: `Confirm ${label.toLowerCase()}` });
  expect(confirm).toBeDisabled();
  await user.type(within(dialog).getByRole("textbox", { name: "Reason (required)" }), "Correct duplicate entry");
  await user.click(confirm);
  expect(view.applyLifecycleAction).toHaveBeenCalledOnce();
  expect(view.applyLifecycleAction).toHaveBeenCalledWith(label === "Reverse" ? "Reverse" : "LockAfterClose", "Correct duplicate entry");
});

it("requires a fresh review after the draft changes", async () => {
  const view = makeView();
  const user = userEvent.setup();
  const { rerender } = render(<JournalLifecycleControls view={view} />);
  await user.click(screen.getByRole("button", { name: "Post" }));
  rerender(<JournalLifecycleControls view={{ ...view, draft: { ...view.draft, version: 5 } }} />);
  expect(screen.getByRole("button", { name: "Confirm post" })).toBeDisabled();
  expect(screen.getByRole("dialog")).toHaveTextContent("The journal changed");
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
});

it("has accessible confirmation semantics and confirms exactly once", async () => {
  const view = makeView();
  const user = userEvent.setup();
  const { container } = render(<JournalLifecycleControls view={view} />);
  await user.click(screen.getByRole("button", { name: "Rebook" }));
  expect((await axe(container)).violations).toEqual([]);
  await user.click(screen.getByRole("button", { name: "Confirm rebook" }));
  expect(view.applyLifecycleAction).toHaveBeenCalledExactlyOnceWith("Rebook", undefined);
});

it("shows the conflict diff and keeps reviewing/discarding separate from sending a command", async () => {
  const view = makeView();
  view.lifecycleConflict = { action: "Post", notes: "  Operator explanation\nSecond line  ", before: view.draft,
    latest: { ...view.draft, version: 5 }, loading: false, unavailableReason: null, reviewedLatest: false,
    changes: [{ label: "Version", before: "4", after: "5" }, { label: "Total debits", before: "500 USD", after: "650 USD" }] };
  view.reviewLatestLifecycleDraft = vi.fn();
  view.discardLifecycleConflict = vi.fn();
  const { container } = render(<JournalLifecycleControls view={view} />);
  expect(screen.getByRole("region", { name: "Journal changed since loaded" })).toHaveTextContent("500 USD");
  expect(screen.getByRole("region", { name: "Journal changed since loaded" })).toHaveTextContent("650 USD");
  expect((await axe(container)).violations).toEqual([]);
  await userEvent.click(screen.getByRole("button", { name: "Review latest" }));
  expect(view.reviewLatestLifecycleDraft).toHaveBeenCalledOnce();
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole("button", { name: "Discard decision" }));
  expect(view.discardLifecycleConflict).toHaveBeenCalledOnce();
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
});

it("preserves exact conflicting decision notes in the next fresh confirmation", async () => {
  const view = makeView();
  const exactNotes = "  Exact correction note\nRetain spacing  ";
  view.lifecycleConflict = { action: "Reverse", notes: exactNotes, before: { ...view.draft, version: 3 },
    latest: view.draft, loading: false, unavailableReason: null, reviewedLatest: true, changes: [] };
  render(<JournalLifecycleControls view={view} />);
  await userEvent.click(screen.getByRole("button", { name: "Reverse" }));
  expect(screen.getByRole("textbox", { name: "Reason (required)" })).toHaveValue(exactNotes);
  await userEvent.click(screen.getByRole("button", { name: "Confirm reverse" }));
  expect(view.applyLifecycleAction).toHaveBeenCalledExactlyOnceWith("Reverse", exactNotes);
});

it("explains an unavailable latest record without offering an automatic retry", async () => {
  const view = makeView();
  view.lifecycleConflict = { action: "Post", notes: "Retain this decision", before: view.draft,
    latest: null, loading: false, unavailableReason: "Latest record unavailable; local draft retained.", reviewedLatest: false, changes: [] };
  render(<JournalLifecycleControls view={view} />);
  expect(screen.getByRole("alert")).toHaveTextContent("local draft retained");
  expect(screen.getByRole("button", { name: "Review latest" })).toBeDisabled();
  expect(view.applyLifecycleAction).not.toHaveBeenCalled();
});
