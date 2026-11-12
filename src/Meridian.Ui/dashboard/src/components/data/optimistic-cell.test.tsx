import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  OptimisticCell,
  OptimisticProvider,
  OptimisticRevertError,
  OptimisticRowBar,
  OptimisticUnlandedError,
  useOptimistic,
  useOptimisticLedger,
  type OptimisticEdit,
  type OptimisticLedger,
  type OptimisticProviderProps
} from "./optimistic-cell";

const percent = (value: unknown) => `${String(value)}%`;

interface Deferred {
  promise: Promise<void | { receipt?: string }>;
  resolve: (value?: { receipt?: string }) => void;
  reject: (error: Error) => void;
}

function deferred(): Deferred {
  let resolve: Deferred["resolve"] = () => {};
  let reject: Deferred["reject"] = () => {};
  const promise = new Promise<void | { receipt?: string }>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** A parent that keeps the model the way the package contract asks: value on confirmed, prior on reverted. */
function Harness({
  initial = { r1: 12, r2: 40 },
  provider,
  bar = false,
  cell = {},
  onLedger
}: {
  initial?: Record<string, number | string>;
  provider: Omit<OptimisticProviderProps, "children">;
  bar?: boolean;
  cell?: Partial<React.ComponentProps<typeof OptimisticCell>>;
  onLedger?: (ledger: OptimisticLedger) => void;
}) {
  const [model, setModel] = useState(initial);
  const onSettle = (edit: OptimisticEdit) => {
    provider.onSettle?.(edit);
    if (edit.status === "confirmed") {
      setModel((rows) => ({ ...rows, [edit.rowId]: edit.value as string }));
    }
    if (edit.status === "reverted") {
      setModel((rows) => ({ ...rows, [edit.rowId]: edit.prior as string }));
    }
  };
  return (
    <OptimisticProvider {...provider} onSettle={onSettle}>
      {onLedger ? <Probe onLedger={onLedger} /> : null}
      {bar ? <OptimisticRowBar rowIds={Object.keys(model)} label="weight edits" /> : null}
      {Object.entries(model).map(([rowId, value]) => (
        <OptimisticCell
          key={rowId}
          rowId={rowId}
          field="weight"
          label={`${rowId} weight`}
          value={value}
          mode="number"
          format={percent}
          {...cell}
        />
      ))}
    </OptimisticProvider>
  );
}

function Probe({ onLedger }: { onLedger: (ledger: OptimisticLedger) => void }) {
  onLedger(useOptimisticLedger());
  return null;
}

async function editCell(user: ReturnType<typeof userEvent.setup>, name: RegExp, draft: string) {
  await user.click(screen.getByRole("button", { name }));
  const input = screen.getByRole("spinbutton");
  await user.clear(input);
  await user.type(input, `${draft}{Enter}`);
}

function cellRoot(rowLabel: RegExp): HTMLElement {
  const root = screen.getByRole("button", { name: rowLabel }).closest<HTMLElement>(".mds-opt");
  if (!root) {
    throw new Error("cell root not found");
  }
  return root;
}

describe("OptimisticCell (Concrete)", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("reads Provisional in a polite live region until the commit resolves, then Confirmed", async () => {
    const user = userEvent.setup();
    const pending = deferred();
    const commit = vi.fn((_edit: OptimisticEdit) => pending.promise);
    render(<Harness provider={{ commit, revert: vi.fn().mockResolvedValue(undefined) }} />);

    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "idle");
    await editCell(user, /Edit r1 weight/, "15");

    const root = cellRoot(/Edit r1 weight: 15%, was 12%/);
    expect(root).toHaveAttribute("data-status", "pending");
    const mark = root.querySelector(".mds-opt__mark");
    expect(mark).toHaveAttribute("aria-live", "polite");
    expect(mark).toHaveTextContent("Provisional");
    expect(commit).toHaveBeenCalledTimes(1);
    expect(commit.mock.calls[0][0]).toMatchObject({ rowId: "r1", field: "weight", prior: 12, value: "15", status: "pending" });
    // Not editable and not revertible while the write is in flight.
    expect(screen.getByRole("button", { name: /Edit r1 weight/ })).toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByRole("button", { name: /Revert r1 weight/ })).not.toBeInTheDocument();

    await act(async () => {
      pending.resolve({ receipt: "chg-1" });
    });

    expect(root).toHaveAttribute("data-status", "confirmed");
    expect(mark).toHaveTextContent("Confirmed");
    expect(screen.getByRole("button", { name: "Edit r1 weight: 15%, was 12%" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Revert r1 weight to 12%" })).toBeInTheDocument();
  });

  it("restores the prior value and shows the reason when the commit throws, until dismissed", async () => {
    const user = userEvent.setup();
    const commit = vi.fn().mockRejectedValue(new Error("Policy · weight floor is 10% (RISK-014)."));
    render(<Harness provider={{ commit, revert: vi.fn() }} />);

    await editCell(user, /Edit r1 weight/, "5");

    const root = await waitFor(() => {
      const found = cellRoot(/Edit r1 weight: 12%/);
      expect(found).toHaveAttribute("data-status", "rejected");
      return found;
    });
    expect(root.querySelector(".mds-opt__mark")).toHaveTextContent("Rejected");
    const note = within(root).getByRole("status");
    expect(note).toHaveTextContent("Rejected");
    expect(note).toHaveTextContent("5% → back to 12%");
    expect(note).toHaveTextContent("Policy · weight floor is 10% (RISK-014).");
    expect(screen.queryByRole("button", { name: /Revert r1 weight/ })).not.toBeInTheDocument();

    await user.click(within(note).getByRole("button", { name: "Dismiss" }));
    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "idle");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("does not write when the committed draft equals the value on screen", async () => {
    const user = userEvent.setup();
    const commit = vi.fn().mockResolvedValue(undefined);
    render(<Harness provider={{ commit }} />);

    await editCell(user, /Edit r1 weight/, "12");

    expect(commit).not.toHaveBeenCalled();
    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "idle");
  });

  it("keeps the validator in charge of what reaches the ledger", async () => {
    const user = userEvent.setup();
    const commit = vi.fn().mockResolvedValue(undefined);
    render(
      <Harness
        provider={{ commit }}
        cell={{ validator: (draft) => (Number(draft) <= 100 ? true : "At most 100"), hint: "Percent of book" }}
      />
    );

    await editCell(user, /Edit r1 weight/, "250");

    expect(screen.getByRole("alert")).toHaveTextContent("At most 100");
    expect(commit).not.toHaveBeenCalled();
  });

  it("asks for a stated reason before reverting, in a named dialog with focus on the reason field", async () => {
    const user = userEvent.setup();
    const revert = vi.fn().mockResolvedValue(undefined);
    const settled: string[] = [];
    render(
      <Harness
        provider={{
          commit: vi.fn().mockResolvedValue(undefined),
          revert,
          onSettle: (edit) => settled.push(edit.status)
        }}
      />
    );
    await editCell(user, /Edit r1 weight/, "15");
    const opener = await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    expect(opener).toHaveTextContent("Revert…");

    await user.click(opener);

    const dialog = screen.getByRole("dialog", { name: "Revert this cell" });
    expect(dialog).not.toHaveAttribute("aria-modal");
    expect(dialog).toHaveTextContent("Puts 15% back to 12%.");
    expect(dialog).toHaveTextContent("The revert is sent as its own write, not as an erasure of the first.");
    const reason = within(dialog).getByRole("textbox", { name: "Reason · required" });
    expect(reason).toHaveFocus();
    const confirm = within(dialog).getByRole("button", { name: "Revert cell" });
    expect(confirm).toBeDisabled();

    await user.type(reason, "   ");
    expect(confirm).toBeDisabled();
    await user.type(reason, "Entered against the wrong sleeve");
    expect(confirm).toBeEnabled();
    await user.click(confirm);

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "reverted"));
    expect(revert).toHaveBeenCalledTimes(1);
    expect(revert.mock.calls[0][0]).toHaveLength(1);
    expect(revert.mock.calls[0][0][0]).toMatchObject({ rowId: "r1", prior: 12, value: "15" });
    expect(revert.mock.calls[0][1]).toBe("Entered against the wrong sleeve");
    expect(settled).toEqual(["confirmed", "reverted"]);
    // The revert is recorded beside the first write, with its reason, not erased.
    const note = within(cellRoot(/Edit r1 weight: 12%/)).getByRole("status");
    expect(note).toHaveTextContent("Reverted");
    expect(note).toHaveTextContent("15% → 12%");
    expect(note).toHaveTextContent("Entered against the wrong sleeve");
    expect(cellRoot(/Edit r1 weight: 12%/).querySelector(".mds-opt__mark")).toHaveTextContent("Reverted");
  });

  it("returns focus to Revert… when the reason form is closed with Escape or Cancel", async () => {
    const user = userEvent.setup();
    const onSurfaceKeyDown = vi.fn();
    render(
      <div onKeyDown={onSurfaceKeyDown}>
        <Harness provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn() }} />
      </div>
    );
    await editCell(user, /Edit r1 weight/, "15");
    const name = "Revert r1 weight to 12%";

    await user.click(await screen.findByRole("button", { name }));
    expect(screen.getByRole("textbox", { name: "Reason · required" })).toHaveFocus();
    onSurfaceKeyDown.mockClear();
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name })).toHaveFocus();
    // Escape closed the form only; it did not reach an enclosing drawer or dialog.
    expect(onSurfaceKeyDown).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name })).toHaveFocus();
    expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed");
  });

  it("returns focus to the cell when the revert is confirmed and Revert… is gone", async () => {
    const user = userEvent.setup();
    const reverting = deferred();
    render(
      <Harness
        provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn(() => reverting.promise as Promise<void>) }}
      />
    );
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong sleeve{Enter}");

    const root = cellRoot(/Edit r1 weight/);
    expect(root).toHaveAttribute("data-status", "reverting");
    expect(root.querySelector(".mds-opt__mark")).toHaveTextContent("Reverting");
    expect(screen.queryByRole("button", { name: /Revert r1 weight/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Edit r1 weight/ })).toHaveFocus();
    expect(document.body).not.toHaveFocus();

    await act(async () => {
      reverting.resolve();
    });
    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "reverted");
  });

  it("stays Confirmed and says so when the compensating write fails", async () => {
    const user = userEvent.setup();
    const settled: string[] = [];
    render(
      <Harness
        provider={{
          commit: vi.fn().mockResolvedValue(undefined),
          revert: vi.fn().mockRejectedValue(new Error("compensation denied")),
          onSettle: (edit) => settled.push(edit.status)
        }}
      />
    );
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong sleeve{Enter}");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Revert not applied; the value is still 15%. compensation denied");
    expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed");
    expect(screen.getByRole("button", { name: "Revert r1 weight to 12%" })).toBeInTheDocument();
    expect(settled).toEqual(["confirmed"]);
  });

  it("offers no revert when the provider has no compensating write", async () => {
    const user = userEvent.setup();
    render(<Harness provider={{ commit: vi.fn().mockResolvedValue(undefined) }} bar />);

    await editCell(user, /Edit r1 weight/, "15");

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed"));
    expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Edit ledger · weight edits" })).toHaveTextContent("1 confirmed");
  });

  it("says where the reason is kept and what Confirmed does not mean, when the screen provides it", async () => {
    const user = userEvent.setup();
    render(
      <Harness
        provider={{
          commit: vi.fn().mockResolvedValue(undefined),
          revert: vi.fn().mockResolvedValue(undefined),
          reasonDisclosure: "Kept in this browser session only."
        }}
        cell={{ confirmedNote: "Saved. Not live until restart.", notePlacement: "inline" }}
      />
    );
    await editCell(user, /Edit r1 weight/, "15");

    await waitFor(() =>
      expect(cellRoot(/Edit r1 weight: 15%/).querySelector(".mds-opt__mark")).toHaveTextContent(
        "ConfirmedSaved. Not live until restart."
      )
    );
    await user.click(screen.getByRole("button", { name: "Revert r1 weight to 12%" }));
    const reason = screen.getByRole("textbox", { name: "Reason · required" });
    expect(reason).toHaveAccessibleDescription("Kept in this browser session only.");
    expect(screen.getByRole("dialog")).toHaveClass("mds-opt__note--inline");

    await user.type(reason, "Wrong sleeve{Enter}");
    const note = await screen.findByRole("status");
    expect(note).toHaveTextContent("Wrong sleeve");
    expect(note).toHaveTextContent("Kept in this browser session only.");
  });

  it("drops a confirmed entry when the model value moves on through another write path", async () => {
    const user = userEvent.setup();
    const commit = vi.fn().mockResolvedValue(undefined);
    function External() {
      const [value, setValue] = useState(12);
      return (
        <OptimisticProvider commit={commit} revert={vi.fn()} onSettle={(edit) => {
          if (edit.status === "confirmed") {
            setValue(Number(edit.value));
          }
        }}>
          <OptimisticCell rowId="r1" field="weight" label="r1 weight" value={value} mode="number" format={percent} />
          <button type="button" onClick={() => setValue(70)}>Full-form save</button>
        </OptimisticProvider>
      );
    }
    render(<External />);
    await editCell(user, /Edit r1 weight/, "15");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed"));

    await user.click(screen.getByRole("button", { name: "Full-form save" }));

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 70%/)).toHaveAttribute("data-status", "idle"));
    expect(screen.queryByRole("button", { name: /Revert r1 weight/ })).not.toBeInTheDocument();
  });

  it("logs a failing onCommit observer without stopping the write", async () => {
    const user = userEvent.setup();
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const commit = vi.fn().mockResolvedValue(undefined);
    render(
      <Harness provider={{ commit }} cell={{ onCommit: () => { throw new Error("observer failed"); } }} />
    );

    await editCell(user, /Edit r1 weight/, "15");

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed"));
    expect(commit).toHaveBeenCalledTimes(1);
    expect(consoleError).toHaveBeenCalledWith("OptimisticCell: onCommit observer failed", expect.any(Error));
  });

  it("refuses to render without a provider, so a cell can never confirm a write nobody made", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<OptimisticCell rowId="r1" field="weight" value={12} />)).toThrow(
      "OptimisticCell must be rendered inside an OptimisticProvider with a commit handler."
    );
    expect(() => render(<OptimisticRowBar />)).toThrow(
      "OptimisticRowBar must be rendered inside an OptimisticProvider with a commit handler."
    );
  });

  it("exposes the ledger under the package's hook name and reports changes to onChange", async () => {
    const user = userEvent.setup();
    expect(useOptimistic).toBe(useOptimisticLedger);
    const onChange = vi.fn();
    let ledger: OptimisticLedger | null = null;
    render(
      <Harness
        provider={{ commit: vi.fn().mockResolvedValue({ receipt: "chg-7" }), onChange }}
        onLedger={(current) => { ledger = current; }}
      />
    );

    await editCell(user, /Edit r1 weight/, "15");

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed"));
    const current = ledger as OptimisticLedger | null;
    expect(current?.latest("r1", "weight")).toMatchObject({ status: "confirmed", receipt: "chg-7" });
    expect(onChange.mock.calls.map(([entries]) => (entries as OptimisticEdit[]).map((entry) => entry.status))).toEqual([
      ["pending"],
      ["confirmed"]
    ]);
  });

  it("has no axe violations in any state", async () => {
    const user = userEvent.setup();
    const commit = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error("Policy · weight floor is 10%."));
    const { container } = render(
      <main>
        <Harness provider={{ commit, revert: vi.fn().mockResolvedValue(undefined), reasonDisclosure: "Session only." }} bar />
      </main>
    );
    expect((await axe(container)).violations).toEqual([]);

    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r2 weight/, "5");
    await waitFor(() => expect(cellRoot(/Edit r2 weight: 40%/)).toHaveAttribute("data-status", "rejected"));
    expect((await axe(container)).violations).toEqual([]);

    await user.click(screen.getByRole("button", { name: "Revert r1 weight to 12%" }));
    expect((await axe(container)).violations).toEqual([]);

    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong sleeve{Enter}");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "reverted"));
    expect((await axe(container)).violations).toEqual([]);
  });
});

describe("OptimisticRowBar (Concrete)", () => {
  it("renders nothing without edits unless an empty label is given", () => {
    const { container, rerender } = render(
      <OptimisticProvider commit={vi.fn()}>
        <OptimisticRowBar rowIds="r1" />
      </OptimisticProvider>
    );
    expect(container).toBeEmptyDOMElement();

    rerender(
      <OptimisticProvider commit={vi.fn()}>
        <OptimisticRowBar rowIds="r1" emptyLabel="No edits yet — every write will be listed here with its fate." />
      </OptimisticProvider>
    );
    expect(screen.getByText("No edits yet — every write will be listed here with its fate.")).toBeInTheDocument();
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
  });

  it("tallies the fates in words and reverts every confirmed edit newest first under one reason", async () => {
    const user = userEvent.setup();
    const commit = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error("Policy · weight floor is 10%."));
    const revert = vi.fn().mockResolvedValue(undefined);
    render(<Harness initial={{ r1: 12, r2: 40, r3: 20 }} provider={{ commit, revert }} bar />);

    expect(screen.queryByRole("group")).not.toBeInTheDocument();
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r2 weight/, "45");
    await screen.findByRole("button", { name: "Revert r2 weight to 40%" });
    await editCell(user, /Edit r3 weight/, "5");

    const bar = await screen.findByRole("group", { name: "Edit ledger · weight edits" });
    await waitFor(() => expect(bar).toHaveTextContent("1 rejected"));
    expect(bar).toHaveTextContent("3 weight edits");
    expect(bar).toHaveTextContent("2 confirmed");
    expect(bar).not.toHaveTextContent("provisional");

    await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
    const dialog = within(bar).getByRole("dialog", { name: "Revert 2 confirmed edits" });
    expect(dialog).toHaveTextContent(
      "Every confirmed cell in 3 rows goes back to its prior value under one reason. Provisional edits are left to settle first."
    );
    expect(within(dialog).getByRole("button", { name: "Revert batch" })).toBeDisabled();
    await user.type(within(dialog).getByRole("textbox", { name: "Reason · required" }), "End-of-day unwind");
    await user.click(within(dialog).getByRole("button", { name: "Revert batch" }));

    await waitFor(() => expect(bar).toHaveTextContent("2 reverted"));
    expect(revert).toHaveBeenCalledTimes(1);
    expect(revert.mock.calls[0][0].map((edit: OptimisticEdit) => edit.rowId)).toEqual(["r2", "r1"]);
    expect(revert.mock.calls[0][1]).toBe("End-of-day unwind");
    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "reverted");
    expect(cellRoot(/Edit r2 weight: 40%/)).toHaveAttribute("data-status", "reverted");
    // Nothing is left to revert: the batch control says so and focus stays on the bar.
    const spent = within(bar).getByRole("button", { name: "Revert row…" });
    expect(spent).toBeDisabled();
    expect(spent).toHaveAttribute("title", "No confirmed edit to revert. Provisional edits settle first.");
    expect(bar).toHaveFocus();
  });

  it("returns focus to the batch control when its reason form is closed with Escape", async () => {
    const user = userEvent.setup();
    render(
      <Harness provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn() }} bar />
    );
    await editCell(user, /Edit r1 weight/, "15");
    const batch = await screen.findByRole("button", { name: "Revert 1 confirmed…" });

    await user.click(batch);
    expect(screen.getByRole("dialog", { name: "Revert 1 confirmed edit" })).toBeInTheDocument();
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Revert 1 confirmed…" })).toHaveFocus();
  });

  it("counts a provisional edit and leaves it out of the batch", async () => {
    const user = userEvent.setup();
    const held = deferred();
    const commit = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockImplementationOnce(() => held.promise);
    render(<Harness provider={{ commit, revert: vi.fn().mockResolvedValue(undefined) }} bar />);

    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r2 weight/, "45");

    const bar = screen.getByRole("group", { name: "Edit ledger · weight edits" });
    expect(bar).toHaveTextContent("1 provisional");
    expect(bar).toHaveTextContent("1 confirmed");
    expect(within(bar).getByRole("button", { name: "Revert 1 confirmed…" })).toBeEnabled();

    await act(async () => {
      held.resolve();
    });
    expect(bar).toHaveTextContent("2 confirmed");
  });
});

/** One cell whose model the test can also change from outside the ledger, like a second write path. */
function Controlled({
  provider,
  bar = false,
  cell = {}
}: {
  provider: Omit<OptimisticProviderProps, "children">;
  bar?: boolean;
  cell?: Partial<React.ComponentProps<typeof OptimisticCell>>;
}) {
  const [value, setValue] = useState<number | string>(12);
  const [mounted, setMounted] = useState(true);
  const onSettle = (edit: OptimisticEdit) => {
    provider.onSettle?.(edit);
    if (edit.status === "confirmed") {
      setValue(edit.value as string);
    }
    if (edit.status === "reverted") {
      setValue(edit.prior as string);
    }
  };
  return (
    <OptimisticProvider {...provider} onSettle={onSettle}>
      {bar ? <OptimisticRowBar rowIds="r1" label="weight edits" /> : null}
      {mounted ? (
        <OptimisticCell rowId="r1" field="weight" label="r1 weight" value={value} mode="number" format={percent} {...cell} />
      ) : null}
      <button type="button" onClick={() => setValue(80)}>Full-form save 80</button>
      <button type="button" onClick={() => setValue(12)}>Full-form save 12</button>
      <button type="button" onClick={() => setMounted((current) => !current)}>Toggle row</button>
    </OptimisticProvider>
  );
}

describe("OptimisticCell beside another write path", () => {
  it("clears every stacked confirmed edit when the other path puts the original value back", async () => {
    const user = userEvent.setup();
    render(<Controlled provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn() }} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r1 weight/, "20");
    await screen.findByRole("button", { name: "Revert r1 weight to 15%" });

    await user.click(screen.getByRole("button", { name: "Full-form save 12" }));

    // Not the first edit resurfacing as "15%, was 12% · Confirmed" over a model that holds 12.
    await waitFor(() => expect(cellRoot(/^Edit r1 weight: 12%$/)).toHaveAttribute("data-status", "idle"));
    expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
  });

  it("takes the older confirmed edit out of the batch once the other path has moved the value, even beneath a Reverted note", async () => {
    const user = userEvent.setup();
    const revert = vi.fn().mockResolvedValue(undefined);
    render(<Controlled provider={{ commit: vi.fn().mockResolvedValue(undefined), revert }} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r1 weight/, "20");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 15%" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Second one was wrong{Enter}");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "reverted"));
    const bar = screen.getByRole("group", { name: "Edit ledger · weight edits" });
    expect(bar).toHaveTextContent("1 confirmed");
    expect(within(bar).getByRole("button", { name: "Revert 1 confirmed…" })).toBeEnabled();

    // The Reverted note is still on screen when the other path writes 80.
    await user.click(screen.getByRole("button", { name: "Full-form save 80" }));

    await waitFor(() => expect(bar).not.toHaveTextContent("confirmed"));
    expect(bar).toHaveTextContent("1 weight edits");
    expect(bar).toHaveTextContent("1 reverted");
    expect(within(bar).getByRole("button", { name: "Revert row…" })).toBeDisabled();
    // The revert that did happen keeps its record and its reason.
    const note = within(cellRoot(/Edit r1 weight: 80%/)).getByRole("status");
    expect(note).toHaveTextContent("20% → 15%");
    expect(note).toHaveTextContent("Second one was wrong");
    expect(revert).toHaveBeenCalledTimes(1);
  });

  it("does not bring back an older rejection when the retry that succeeded is superseded", async () => {
    const user = userEvent.setup();
    const commit = vi.fn()
      .mockRejectedValueOnce(new Error("Too many requests."))
      .mockResolvedValueOnce(undefined);
    render(<Controlled provider={{ commit, revert: vi.fn() }} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "rejected"));
    await editCell(user, /Edit r1 weight/, "15");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed"));

    await user.click(screen.getByRole("button", { name: "Full-form save 80" }));

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 80%/)).toHaveAttribute("data-status", "idle"));
    expect(screen.queryByText(/back to 12%/)).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
  });

  it("does not show an old confirmed edit over the model when the cell is mounted again on the original value", async () => {
    const user = userEvent.setup();
    render(<Controlled provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn() }} />);
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });

    // The row goes away, the other path resets the value, and the row comes back.
    await user.click(screen.getByRole("button", { name: "Toggle row" }));
    await user.click(screen.getByRole("button", { name: "Full-form save 12" }));
    await user.click(screen.getByRole("button", { name: "Toggle row" }));

    await waitFor(() => expect(cellRoot(/^Edit r1 weight: 12%$/)).toHaveAttribute("data-status", "idle"));
    expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
  });

  it("closes the cell's reason form when its edit is reverted from the row bar", async () => {
    const user = userEvent.setup();
    const reverting = deferred();
    render(
      <Controlled
        provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn(() => reverting.promise as Promise<void>) }}
        bar
      />
    );
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    expect(screen.getByRole("dialog", { name: "Revert this cell" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Revert 1 confirmed…" }));
    await user.type(
      within(screen.getByRole("dialog", { name: "Revert 1 confirmed edit" })).getByRole("textbox", { name: "Reason · required" }),
      "From the bar{Enter}"
    );

    expect(screen.queryByRole("dialog", { name: "Revert this cell" })).not.toBeInTheDocument();
    // Focus stays with the bar, where the operator is working; the closing cell form does not take it.
    expect(screen.getByRole("button", { name: /Edit r1 weight/ })).not.toHaveFocus();
    expect(screen.getByRole("group", { name: "Edit ledger · weight edits" })).toHaveFocus();
    await act(async () => {
      reverting.resolve();
    });
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "reverted"));
    // It does not reopen on a later confirmed edit.
    await user.click(within(cellRoot(/Edit r1 weight: 12%/)).getByRole("button", { name: "Dismiss" }));
    await editCell(user, /Edit r1 weight/, "30");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

describe("OptimisticCell when a write gets no answer", () => {
  const unanswered = () => new OptimisticUnlandedError("No answer came back, so it is not known whether this was saved.");

  it("reads Unlanded, not Rejected, and does not claim the value went back", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <main>
        <Controlled provider={{ commit: vi.fn().mockRejectedValue(unanswered()), revert: vi.fn() }} bar />
      </main>
    );

    await editCell(user, /Edit r1 weight/, "15");

    const root = await waitFor(() => {
      const found = cellRoot(/Edit r1 weight: 12%/);
      expect(found).toHaveAttribute("data-status", "unlanded");
      return found;
    });
    expect(root.querySelector(".mds-opt__mark")).toHaveTextContent("Unlanded");
    const note = within(root).getByRole("status");
    expect(note).toHaveTextContent("Unlanded");
    expect(note).toHaveTextContent("15% sent · no answer");
    expect(note).toHaveTextContent("No answer came back, so it is not known whether this was saved.");
    expect(root).not.toHaveTextContent("Rejected");
    expect(root).not.toHaveTextContent("back to");
    expect(screen.getByRole("group", { name: "Edit ledger · weight edits" })).toHaveTextContent("1 unlanded");
    expect(screen.queryByRole("button", { name: /Revert r1 weight/ })).not.toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);

    await user.click(within(note).getByRole("button", { name: "Dismiss" }));
    expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "idle");
  });

  it("becomes Confirmed when the model is later found holding the value that was sent", async () => {
    const user = userEvent.setup();
    render(<Controlled provider={{ commit: vi.fn().mockRejectedValue(unanswered()), revert: vi.fn() }} />);
    await editCell(user, /Edit r1 weight/, "80");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 12%/)).toHaveAttribute("data-status", "unlanded"));

    // A re-read shows the write did land.
    await user.click(screen.getByRole("button", { name: "Full-form save 80" }));

    await waitFor(() => expect(cellRoot(/Edit r1 weight: 80%, was 12%/)).toHaveAttribute("data-status", "confirmed"));
    expect(screen.getByRole("button", { name: "Revert r1 weight to 12%" })).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("holds back the batch revert of a confirmed edit beneath an unlanded one, and says why", async () => {
    const user = userEvent.setup();
    const commit = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(unanswered());
    render(<Controlled provider={{ commit, revert: vi.fn() }} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r1 weight/, "20");
    await waitFor(() => expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "unlanded"));

    const bar = screen.getByRole("group", { name: "Edit ledger · weight edits" });
    expect(bar).toHaveTextContent("1 confirmed");
    const batch = within(bar).getByRole("button", { name: "Revert row…" });
    expect(batch).toBeDisabled();
    expect(batch).toHaveAttribute(
      "title",
      "Every confirmed edit here sits beneath a newer provisional or unlanded edit of the same field. That one settles or is dismissed first."
    );

    await user.click(within(cellRoot(/Edit r1 weight: 15%/)).getByRole("button", { name: "Dismiss" }));
    expect(within(bar).getByRole("button", { name: "Revert 1 confirmed…" })).toBeEnabled();
  });

  it("says a revert is not confirmed, rather than not applied, when the compensating write got no answer", async () => {
    const user = userEvent.setup();
    render(
      <Controlled
        provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn().mockRejectedValue(unanswered()) }}
      />
    );
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong sleeve{Enter}");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Revert not confirmed. No answer came back, so it is not known whether this was saved."
    );
    expect(alert).not.toHaveTextContent("the value is still");
    expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed");

    // A re-read shows the prior: the revert landed, and it is recorded with the stated reason.
    await user.click(screen.getByRole("button", { name: "Full-form save 12" }));
    const note = await screen.findByRole("status");
    expect(note).toHaveTextContent("Reverted");
    expect(note).toHaveTextContent("Wrong sleeve");
  });
});

describe("OptimisticCell and OptimisticRowBar details", () => {
  it.each([
    ["a Rejected note", (): Partial<OptimisticProviderProps> => ({ commit: vi.fn().mockRejectedValue(new Error("Policy floor.")) })],
    ["an Unlanded note", (): Partial<OptimisticProviderProps> => ({ commit: vi.fn().mockRejectedValue(new OptimisticUnlandedError("No answer.")) })]
  ])("returns focus to the cell when %s is dismissed from the keyboard", async (_name, provider) => {
    const user = userEvent.setup();
    render(<Controlled provider={{ commit: vi.fn(), revert: vi.fn(), ...provider() }} />);
    await editCell(user, /Edit r1 weight/, "15");
    const dismiss = await screen.findByRole("button", { name: "Dismiss" });

    dismiss.focus();
    await user.keyboard("{Enter}");

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit r1 weight: 12%" })).toHaveFocus();
  });

  it("returns focus to the cell when a Reverted note is dismissed from the keyboard", async () => {
    const user = userEvent.setup();
    render(
      <Controlled provider={{ commit: vi.fn().mockResolvedValue(undefined), revert: vi.fn().mockResolvedValue(undefined) }} />
    );
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong sleeve{Enter}");
    const dismiss = await screen.findByRole("button", { name: "Dismiss" });

    dismiss.focus();
    await user.keyboard("{Enter}");

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit r1 weight: 12%" })).toHaveFocus();
    expect(document.body).not.toHaveFocus();
  });

  it("disables Revert… and the open form's confirm, with the reason, while the screen blocks reverts", async () => {
    const user = userEvent.setup();
    const revert = vi.fn().mockResolvedValue(undefined);
    const provider = { commit: vi.fn().mockResolvedValue(undefined), revert };
    const blocked = "This record is being saved. Wait for that to finish.";
    const { rerender } = render(<Controlled provider={provider} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await user.click(await screen.findByRole("button", { name: "Revert r1 weight to 12%" }));
    const reason = screen.getByRole("textbox", { name: "Reason · required" });
    await user.type(reason, "Wrong sleeve");
    expect(screen.getByRole("button", { name: "Revert cell" })).toBeEnabled();

    // Another write to the record starts while the form is open.
    rerender(<Controlled provider={provider} bar cell={{ revertBlockedReason: blocked }} />);
    const confirm = screen.getByRole("button", { name: "Revert cell" });
    expect(confirm).toBeDisabled();
    expect(confirm).toHaveAttribute("title", blocked);
    await user.type(reason, "{Enter}");
    expect(revert).not.toHaveBeenCalled();
    expect(reason).toHaveValue("Wrong sleeve");

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    const opener = screen.getByRole("button", { name: "Revert r1 weight to 12%" });
    expect(opener).toBeDisabled();
    expect(opener).toHaveAttribute("title", blocked);
  });

  it("disables the batch control, with the reason, while the screen blocks reverts", async () => {
    const user = userEvent.setup();
    const blocked = "A record is being saved. Wait for that to finish.";
    function Bar({ reason }: { reason: string | null }) {
      return (
        <OptimisticProvider commit={vi.fn().mockResolvedValue(undefined)} revert={vi.fn()}>
          <OptimisticRowBar label="weight edits" revertBlockedReason={reason} />
          <OptimisticCell rowId="r1" field="weight" label="r1 weight" value={12} mode="number" format={percent} />
        </OptimisticProvider>
      );
    }
    const { rerender } = render(<Bar reason={null} />);
    await editCell(user, /Edit r1 weight/, "15");
    expect(await screen.findByRole("button", { name: "Revert 1 confirmed…" })).toBeEnabled();

    rerender(<Bar reason={blocked} />);

    const batch = screen.getByRole("button", { name: "Revert 1 confirmed…" });
    expect(batch).toBeDisabled();
    expect(batch).toHaveAttribute("title", blocked);
  });

  it("keeps the reverts that landed when a batch fails part of the way through", async () => {
    const user = userEvent.setup();
    const revert = vi.fn(async (edits: OptimisticEdit[]) => {
      // Newest first: r2 is written, then r1 is refused.
      throw new OptimisticRevertError("Store is read-only.", { landed: [edits[0].id] });
    });
    render(<Harness provider={{ commit: vi.fn().mockResolvedValue(undefined), revert }} bar />);
    await editCell(user, /Edit r1 weight/, "15");
    await screen.findByRole("button", { name: "Revert r1 weight to 12%" });
    await editCell(user, /Edit r2 weight/, "45");
    await screen.findByRole("button", { name: "Revert r2 weight to 40%" });
    const bar = screen.getByRole("group", { name: "Edit ledger · weight edits" });

    await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
    await user.type(within(bar).getByRole("textbox", { name: "Reason · required" }), "Wrong desk{Enter}");

    await waitFor(() => expect(cellRoot(/Edit r2 weight: 40%/)).toHaveAttribute("data-status", "reverted"));
    const note = within(cellRoot(/Edit r2 weight: 40%/)).getByRole("status");
    expect(note).toHaveTextContent("45% → 40%");
    expect(note).toHaveTextContent("Wrong desk");
    expect(cellRoot(/Edit r1 weight: 15%/)).toHaveAttribute("data-status", "confirmed");
    expect(screen.getByRole("alert")).toHaveTextContent("Revert not applied; the value is still 15%. Store is read-only.");
    expect(bar).toHaveTextContent("1 confirmed");
    expect(bar).toHaveTextContent("1 reverted");
  });

  it("gives every state border more weight than EditableCell's disabled and hover rules", () => {
    render(
      <OptimisticProvider commit={vi.fn()}>
        <OptimisticCell rowId="r1" field="weight" value={12} />
      </OptimisticProvider>
    );
    const css = document.head.querySelector('style[data-mds="optimistic-cell"]')?.textContent ?? "";
    const editableCss = document.head.querySelector('style[data-mds="editable-cell"]')?.textContent ?? "";
    // A provisional cell is always disabled, and EditableCell clears the border of a disabled
    // display with a two-part selector injected after this stylesheet.
    expect(editableCss).toContain('.mds-editable__display[aria-disabled="true"]{cursor:default;border-color:transparent;}');
    for (const state of ["pending", "confirmed", "rejected", "unlanded"]) {
      expect(css).toContain(`.mds-opt.mds-opt--${state} .mds-editable__display`);
      // No two-part form of the rule is left behind.
      expect(css).not.toMatch(new RegExp(`(^|[^t])\\.mds-opt--${state} \\.mds-editable__display`));
    }
    expect(css).toContain(".mds-opt.mds-opt--pending .mds-editable__display:hover");
  });
});
