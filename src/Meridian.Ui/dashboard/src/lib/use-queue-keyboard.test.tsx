import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";

const rows = [{ id: "one" }, { id: "two" }];
function Queue({ review = () => {} }: { review?: () => void }) {
  const [selected, setSelected] = useState("one");
  const keyboard = useQueueKeyboard({ rows, selectedId: selected, getId: (row: typeof rows[number]) => row.id,
    onSelect: (row: typeof rows[number]) => setSelected(row.id), actions: [{ key: "r", label: "Review", enabled: true, run: review }] });
  return <div onKeyDownCapture={keyboard}>
    {rows.map((row) => <div key={row.id} data-dense-row-id={row.id} tabIndex={row.id === selected ? 0 : -1} aria-label={`Row ${row.id}`}>
      <input aria-label={`Notes ${row.id}`} />
      <div contentEditable="plaintext-only" suppressContentEditableWarning tabIndex={0} aria-label={`Editable ${row.id}`} />
      <details><summary>Evidence {row.id}</summary>Evidence body</details>
      <div role="dialog" aria-label={`Dialog ${row.id}`} tabIndex={0}>Review dialog</div>
    </div>)}
  </div>;
}

describe("queue keyboard boundaries", () => {
  it("moves only a focused queue row and invokes only a declared review capability", async () => {
    const user = userEvent.setup(); const review = vi.fn(); render(<Queue review={review} />);
    screen.getByLabelText("Row one").focus(); await user.keyboard("j");
    await waitFor(() => expect(screen.getByLabelText("Row two")).toHaveFocus());
    await user.keyboard("r"); expect(review).toHaveBeenCalledTimes(1);
    await user.keyboard("ad"); expect(review).toHaveBeenCalledTimes(1);
    await user.keyboard("k"); await waitFor(() => expect(screen.getByLabelText("Row one")).toHaveFocus());
  });
  it("does not capture typing, editable content, disclosures, or dialogs", async () => {
    const user = userEvent.setup(); const review = vi.fn(); render(<Queue review={review} />);
    const notes = screen.getByLabelText("Notes one"); await user.type(notes, "jkrad"); expect(notes).toHaveValue("jkrad");
    const editable = screen.getByLabelText("Editable one"); editable.focus(); await user.keyboard("jkrad"); expect(editable).toHaveFocus();
    const summary = screen.getByText("Evidence one"); summary.focus(); await user.keyboard("jkra"); expect(summary).toHaveFocus();
    const dialog = screen.getByRole("dialog", { name: "Dialog one" }); dialog.focus(); await user.keyboard("jkrad"); expect(dialog).toHaveFocus();
    expect(review).not.toHaveBeenCalled();
  });
});
