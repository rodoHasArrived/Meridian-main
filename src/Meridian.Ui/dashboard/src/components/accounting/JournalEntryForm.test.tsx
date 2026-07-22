import { fireEvent, render, screen } from "@testing-library/react";
import { vi } from "vitest";
import { JournalEntryForm, type JournalLine } from "./JournalEntryForm";

const balanced: JournalLine[] = [
  { account: "Cash", debit: 500, credit: "" },
  { account: "Revenue", debit: "", credit: 500 }
];

const unbalanced: JournalLine[] = [
  { account: "Cash", debit: 500, credit: "" },
  { account: "Revenue", debit: "", credit: 400 }
];

describe("JournalEntryForm", () => {
  it("gates the Post button on a balanced entry", () => {
    render(<JournalEntryForm initialLines={balanced} onPost={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Post entry" })).toBeEnabled();
    expect(screen.getByText("Balanced")).toBeInTheDocument();
  });

  it("disables Post and shows the shortfall when out of balance", () => {
    render(<JournalEntryForm initialLines={unbalanced} onPost={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Post entry" })).toBeDisabled();
    expect(screen.getByText(/Out by/)).toBeInTheDocument();
  });

  it("fires onPost with the current entry when balanced", () => {
    const onPost = vi.fn();
    render(<JournalEntryForm initialLines={balanced} onPost={onPost} />);
    fireEvent.click(screen.getByRole("button", { name: "Post entry" }));
    expect(onPost).toHaveBeenCalledTimes(1);
    expect(onPost.mock.calls[0][0].lines).toHaveLength(2);
  });

  it("emits onChange when a line edits", () => {
    const onChange = vi.fn();
    render(<JournalEntryForm initialLines={balanced} onChange={onChange} />);
    fireEvent.change(screen.getByLabelText("Line 1 debit"), { target: { value: "600" } });
    expect(onChange).toHaveBeenCalled();
  });

  it("generates unique header and datalist IDs for multiple forms", () => {
    render(
      <>
        <JournalEntryForm initialLines={balanced} accounts={["Cash"]} />
        <JournalEntryForm initialLines={balanced} accounts={["Cash"]} />
      </>,
    );
    const dates = screen.getAllByLabelText("Date");
    const lists = screen.getAllByLabelText("Line 1 account").map((input) => input.getAttribute("list"));
    expect(dates[0].id).not.toBe(dates[1].id);
    expect(lists[0]).not.toBe(lists[1]);
  });
});
