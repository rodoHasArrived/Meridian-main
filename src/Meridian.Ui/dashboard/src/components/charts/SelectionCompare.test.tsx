import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import {
  CompareSheet,
  CompareToggle,
  CompareTray,
  SelectionCompareProvider,
  useSelectionCompare,
  type CompareItem,
  type CompareSeriesState,
  type CompareSheetProps,
  type CompareTrayProps,
  type SelectionCompareProviderProps
} from "./SelectionCompare";

const categories = ["a", "b", "c"];
const items: CompareItem[] = [
  { id: "one", title: "One", tag: "PAPER", values: [1, 2, 3] },
  { id: "two", title: "Two", tag: "LIVE", values: [3, 2, 1] },
  { id: "three", title: "Three", values: [2, 2, 2] }
];

interface HarnessProps {
  provider?: Omit<SelectionCompareProviderProps, "children">;
  tray?: CompareTrayProps;
  sheet?: CompareSheetProps;
  onRowClick?: () => void;
  list?: CompareItem[];
}

function Harness({ provider, tray, sheet, onRowClick, list = items }: HarnessProps) {
  return (
    <SelectionCompareProvider categories={categories} {...provider}>
      <CompareTray {...tray} />
      <CompareSheet {...sheet} />
      <ul>
        {list.map((item) => (
          <li key={item.id} data-testid={`row-${item.id}`} onClick={onRowClick}>
            <CompareToggle item={item} />
          </li>
        ))}
      </ul>
    </SelectionCompareProvider>
  );
}

function toggleFor(title: string) {
  return screen.getByRole("button", { name: new RegExp(`comparison: ${title}$`) });
}

describe("SelectionCompare", () => {
  it("toggles aria-pressed and names the action and the item", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    const toggle = screen.getByRole("button", { name: "Add to comparison: One" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(toggle).toHaveAttribute("type", "button");
    expect(toggle).toHaveTextContent("▢Compare");

    await user.click(toggle);

    const pressed = screen.getByRole("button", { name: "Remove from comparison: One" });
    expect(pressed).toHaveAttribute("aria-pressed", "true");
    expect(pressed).toHaveTextContent("▣In compare");
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("never selects the row it sits in", async () => {
    const onRowClick = vi.fn();
    render(<Harness onRowClick={onRowClick} />);

    await userEvent.setup().click(toggleFor("One"));

    expect(onRowClick).not.toHaveBeenCalled();
    expect(toggleFor("One")).toHaveAttribute("aria-pressed", "true");
  });

  it("defaults the cap to six", () => {
    function Probe() {
      return <output>{useSelectionCompare().max}</output>;
    }
    render(<SelectionCompareProvider><Probe /></SelectionCompareProvider>);

    expect(screen.getByRole("status")).toHaveTextContent("6");
  });

  it("refuses an add past the cap in words and leaves the set unchanged", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Harness provider={{ max: 2, onChange }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));

    expect(screen.getAllByRole("button", { pressed: true })).toHaveLength(2);
    const refused = toggleFor("Three");
    expect(refused).toBeDisabled();
    expect(refused).toHaveAttribute("title", "Compare set is full (2)");

    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("2 of 2");
    expect(tray).toHaveTextContent("Set is full (2). Remove one to add another.");

    await user.click(refused);
    expect(onChange).toHaveBeenCalledTimes(2);
    expect(onChange).toHaveBeenLastCalledWith([items[0], items[1]]);
    expect(within(tray).queryByText("Three")).not.toBeInTheDocument();
  });

  it("refuses programmatically too: toggle past the cap is a no-op", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    function Adder() {
      const { toggle, items: current } = useSelectionCompare();
      return (
        <>
          <button type="button" onClick={() => items.forEach((item) => toggle(item))}>add all</button>
          <output>{current.map((item) => item.id).join(",")}</output>
        </>
      );
    }
    render(<SelectionCompareProvider max={2} onChange={onChange}><Adder /></SelectionCompareProvider>);

    await user.click(screen.getByRole("button", { name: "add all" }));

    expect(screen.getByRole("status")).toHaveTextContent("one,two");
    expect(onChange).toHaveBeenCalledTimes(2);
  });

  it("frees the cap again when a member is removed", async () => {
    const user = userEvent.setup();
    render(<Harness provider={{ max: 2 }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    expect(toggleFor("Three")).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Remove One from comparison" }));

    expect(toggleFor("Three")).toBeEnabled();
    expect(toggleFor("Three")).not.toHaveAttribute("title");
    expect(toggleFor("One")).toHaveAttribute("aria-pressed", "false");
  });

  it("hides the tray while empty unless an empty label is given", () => {
    const { rerender } = render(<Harness />);
    expect(screen.queryByRole("region", { name: "Comparison set" })).not.toBeInTheDocument();

    rerender(<Harness tray={{ emptyLabel: "Tick two rows." }} />);
    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("0 of 6");
    expect(tray).toHaveTextContent("Tick two rows.");
    expect(within(tray).queryByRole("button", { name: "Clear" })).not.toBeInTheDocument();
  });

  it("keeps Compare disabled below two, with the reason, and counts from two", async () => {
    const user = userEvent.setup();
    render(<Harness tray={{ label: "runs to compare" }} />);

    await user.click(toggleFor("One"));

    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("1 of 6");
    expect(tray).toHaveTextContent("runs to compare");
    const compare = within(tray).getByRole("button", { name: "Compare" });
    expect(compare).toBeDisabled();
    expect(compare).toHaveAttribute("title", "Add at least two items to compare.");

    await user.click(toggleFor("Two"));

    expect(within(tray).getByRole("button", { name: "Compare 2" })).toBeEnabled();
    expect(within(tray).getByText("PAPER")).toBeInTheDocument();
  });

  it("clears the set from the tray", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Clear" }));

    expect(screen.queryByRole("region", { name: "Comparison set" })).not.toBeInTheDocument();
    expect(screen.queryAllByRole("button", { pressed: true })).toHaveLength(0);
  });

  it("opens the inline sheet from the tray, moves focus into it and reports the callbacks", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onCompare = vi.fn();
    render(<Harness provider={{ onOpenChange, onCompare }} sheet={{ footerLabel: "1Y", footerValue: (item) => `${item.id}-fv` }} />);

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    expect(sheet).toHaveFocus();
    expect(sheet).not.toHaveAttribute("role", "dialog");
    expect(sheet).toHaveTextContent("2 plates · one shared domain · 3 categories");
    expect(sheet).toHaveTextContent(
      "Plates share plot size, domain and ticks; a visible difference is a real difference. Selecting a plate never resizes it."
    );
    expect(within(sheet).getByRole("img", { name: "One — 1Y one-fv; first 1, last 3" })).toBeInTheDocument();
    expect(within(sheet).getByRole("img", { name: "Two — 1Y two-fv; first 3, last 1" })).toBeInTheDocument();
    expect(onOpenChange).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(true);
    expect(onCompare).toHaveBeenCalledWith([items[0], items[1]]);
  });

  it("draws every plate on one shared tick set", async () => {
    const user = userEvent.setup();
    render(<Harness list={[
      { id: "one", title: "One", values: [100, 104, 110] },
      { id: "two", title: "Two", values: [100, 98, 95] }
    ]} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    const ticksOf = (figure: HTMLElement) =>
      Array.from(figure.querySelectorAll("text[x='0']")).map((node) => node.textContent);
    const [first, second] = within(sheet).getAllByRole("img");

    expect(ticksOf(first)).toEqual(ticksOf(second));
    expect(ticksOf(first).length).toBeGreaterThan(1);
    expect(first.getAttribute("viewBox")).toBe(second.getAttribute("viewBox"));
  });

  it("selects a plate by pressed state", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    const plate = within(sheet).getByRole("button", { name: /^One/ });
    expect(plate).toHaveAttribute("aria-pressed", "false");

    await user.click(plate);

    expect(plate).toHaveAttribute("aria-pressed", "true");
    expect(within(sheet).getByRole("button", { name: /^Two/ })).toHaveAttribute("aria-pressed", "false");
  });

  it("closes back to the tray action and keeps the set", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(<Harness provider={{ onOpenChange }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));
    await user.click(screen.getByRole("button", { name: "Close comparison" }));

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Compare 2" })).toHaveFocus();
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
    expect(screen.getAllByRole("button", { pressed: true })).toHaveLength(2);
  });

  it("clears the set from the sheet, which closes it and hands focus to the tray", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(<Harness provider={{ onOpenChange }} tray={{ emptyLabel: "Tick two rows." }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));
    await user.click(screen.getByRole("button", { name: "Clear set" }));

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("0 of 6");
    expect(tray).toHaveFocus();
    expect(onOpenChange).toHaveBeenLastCalledWith(false);

    // A cleared set is closed, not hidden: building a new one does not bring the sheet back.
    await user.click(toggleFor("One"));
    await user.click(toggleFor("Three"));

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    expect(onOpenChange).toHaveBeenCalledTimes(2);
  });

  it("closes the sheet when the set falls below two and reopens it only from the tray action", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(<Harness provider={{ onOpenChange }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));
    await user.click(toggleFor("Two"));

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    expect(onOpenChange.mock.calls).toEqual([[true], [false]]);
    expect(toggleFor("Two")).toHaveFocus();

    await user.click(toggleFor("Three"));

    // Back at two, but nothing opens by itself and focus stays on the toggle that was used.
    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    expect(onOpenChange).toHaveBeenCalledTimes(2);
    expect(toggleFor("Three")).toHaveFocus();

    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    expect(screen.getByRole("region", { name: "Comparison by selection" })).toHaveFocus();
    expect(onOpenChange).toHaveBeenLastCalledWith(true);
  });

  it("keeps the sheet open while a removal leaves two or more", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(<Harness provider={{ onOpenChange }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(toggleFor("Three"));
    await user.click(screen.getByRole("button", { name: "Compare 3" }));
    await user.click(screen.getByRole("button", { name: "Remove Two from comparison" }));

    expect(screen.getByRole("region", { name: "Comparison by selection" })).toHaveTextContent("2 plates");
    expect(onOpenChange).toHaveBeenCalledTimes(1);
  });

  it("moves focus to the neighbouring chip when a chip is removed with the keyboard", async () => {
    const user = userEvent.setup();
    render(<Harness tray={{ emptyLabel: "Tick two rows." }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(toggleFor("Three"));

    // A chip in the middle hands focus to the chip that takes its place.
    screen.getByRole("button", { name: "Remove Two from comparison" }).focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("button", { name: "Remove Three from comparison" })).toHaveFocus();

    // The last chip hands focus back to the one before it.
    await user.keyboard("{Enter}");
    expect(screen.getByRole("button", { name: "Remove One from comparison" })).toHaveFocus();

    // The only chip hands focus to the tray, which stays on screen.
    await user.keyboard("{Enter}");
    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("0 of 6");
    expect(tray).toHaveFocus();
    expect(document.body).not.toHaveFocus();
  });

  it("moves focus to the tray when the set is cleared from the tray with the keyboard", async () => {
    const user = userEvent.setup();
    render(<Harness tray={{ emptyLabel: "Tick two rows." }} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    screen.getByRole("button", { name: "Clear" }).focus();
    await user.keyboard("{Enter}");

    const tray = screen.getByRole("region", { name: "Comparison set" });
    expect(tray).toHaveTextContent("0 of 6");
    expect(tray).toHaveFocus();
  });

  it("hands focus from a sheet that closes under it to the tray", async () => {
    const user = userEvent.setup();
    function DropTwo() {
      const { remove } = useSelectionCompare();
      // Stands in for a host that drops a member on its own, e.g. after a data refresh.
      return <button type="button" onMouseDown={(event) => event.preventDefault()} onClick={() => remove("two")}>host drop</button>;
    }
    render(
      <SelectionCompareProvider categories={categories}>
        <CompareTray />
        <CompareSheet />
        <DropTwo />
        {items.map((item) => <CompareToggle key={item.id} item={item} />)}
      </SelectionCompareProvider>
    );

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));
    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    within(sheet).getByRole("button", { name: /^One/ }).focus();

    // The host button does not take focus (mousedown is prevented), so focus is still in the sheet.
    await user.click(screen.getByRole("button", { name: "host drop" }));

    expect(screen.queryByRole("region", { name: "Comparison by selection" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Comparison set" })).toHaveFocus();
  });

  it("hands the formatter the shared scale so labels can follow the tick step", async () => {
    const user = userEvent.setup();
    const seen: Array<number | null> = [];
    render(
      <Harness
        list={[
          { id: "one", title: "One", values: [100, 100.01, 100.02] },
          { id: "two", title: "Two", values: [100, 100.006, 100.01] }
        ]}
        provider={{
          format: (value, scale) => {
            seen.push(scale.step);
            return value.toFixed(scale.step !== null && scale.step < 0.1 ? 2 : 1);
          }
        }}
      />
    );

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    const [first, second] = within(sheet).getAllByRole("img");
    const ticksOf = (figure: HTMLElement) =>
      Array.from(figure.querySelectorAll("text[x='0']")).map((node) => node.textContent);

    // One step for every call: ticks, end labels, figure names and table cells share a scale.
    expect(new Set(seen)).toEqual(new Set([0.01]));
    expect(ticksOf(first)).toEqual(["100.00", "100.01", "100.02"]);
    expect(ticksOf(second)).toEqual(ticksOf(first));
    expect(first).toHaveAccessibleName("One; first 100.00, last 100.02");
    const row = within(sheet).getByRole("row", { name: /^Two/ });
    expect(within(row).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["100.00", "100.01", "100.01"]);
  });

  it("carries the plotted values as a table", async () => {
    const user = userEvent.setup();
    render(
      <Harness
        provider={{ format: (value) => `${value}%` }}
        sheet={{
          footerLabel: "1Y",
          footerValue: (item) => `${item.id}-fv`,
          itemColumnLabel: "Run",
          tableColumns: [{ id: "len", label: "Length", numeric: true, render: (item) => String(item.values?.length ?? 0) }]
        }}
      />
    );

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const table = screen.getByRole("table", { name: "Comparison by selection: plotted values" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent))
      .toEqual(["Run", "Length", "1Y", "a", "b", "c"]);
    const row = within(table).getByRole("row", { name: /^One/ });
    expect(within(row).getByRole("rowheader")).toHaveTextContent("OnePAPER");
    expect(within(row).getAllByRole("cell").map((cell) => cell.textContent))
      .toEqual(["3", "one-fv", "1%", "2%", "3%"]);
    expect(screen.getByRole("region", { name: "Comparison by selection plotted values scroll area" }))
      .toHaveAttribute("tabindex", "0");
  });

  it("names blank plot categories in the table header", async () => {
    const user = userEvent.setup();
    render(
      <Harness
        provider={{ categories: ["start", "", "end"] }}
        sheet={{ categoryLabels: ["Start", "", "End"] }}
      />
    );

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    const table = screen.getByRole("table", { name: "Comparison by selection: plotted values" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent))
      .toEqual(["Item", "Start", "Point 2", "End"]);
  });

  it("shows an item whose series is loading or unavailable in place, with the reason, and never drops it", async () => {
    const user = userEvent.setup();
    const states: Record<string, CompareSeriesState> = {
      one: { status: "ready", values: [100, 105, 110], footerValue: "+10%" },
      two: { status: "unavailable", reason: "No equity curve is published for this run." },
      three: { status: "loading", text: "Loading equity curve…" }
    };
    const lazyItems: CompareItem[] = [
      { id: "one", title: "One" },
      { id: "two", title: "Two" },
      { id: "three", title: "Three" }
    ];
    render(
      <Harness
        list={lazyItems}
        sheet={{
          footerLabel: "Total return",
          seriesFor: (item) => states[item.id],
          basis: "Rebased to 100 at each item's first point."
        }}
      />
    );

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(toggleFor("Three"));
    await user.click(screen.getByRole("button", { name: "Compare 3" }));

    const sheet = screen.getByRole("region", { name: "Comparison by selection" });
    expect(sheet).toHaveTextContent("1 of 3 plates drawn · 1 loading · 1 unavailable · one shared domain · 3 categories");
    expect(sheet).toHaveTextContent("Rebased to 100 at each item's first point.");
    expect(within(sheet).getAllByRole("img")).toHaveLength(1);
    expect(within(sheet).getByRole("img", { name: "One — Total return +10%; first 100, last 110" })).toBeInTheDocument();
    expect(within(sheet).getAllByText("Unavailable. No equity curve is published for this run.")).toHaveLength(2);
    expect(within(sheet).getAllByText("Loading equity curve…")).toHaveLength(2);

    const table = within(sheet).getByRole("table");
    expect(within(table).getByRole("row", { name: /^Two/ }))
      .toHaveTextContent("Unavailable. No equity curve is published for this run.");
    expect(within(table).getAllByRole("row")).toHaveLength(4);
  });

  it("says so when an item brings no series at all", async () => {
    const user = userEvent.setup();
    render(<Harness list={[{ id: "one", title: "One", values: [1, 2, 3] }, { id: "two", title: "Two" }]} />);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    await user.click(screen.getByRole("button", { name: "Compare 2" }));

    expect(screen.getAllByText("Unavailable. No series was provided for this item.").length).toBeGreaterThan(0);
  });

  it("lets a host rename the toggle, tray and sheet actions", async () => {
    const user = userEvent.setup();
    render(
      <SelectionCompareProvider categories={categories}>
        <CompareTray
          regionLabel="Equity path comparison set"
          compareLabel={(count) => `Chart ${count} equity paths`}
          removeLabel={(item) => `Remove ${item.title} from the equity path set`}
        />
        <CompareSheet title="Equity paths on one scale" closeLabel="Close equity path chart" />
        {items.map((item) => (
          <CompareToggle
            key={item.id}
            item={item}
            compact
            actionLabel={(candidate) => `Chart equity path for ${candidate.title}`}
            fullReason={(max) => `Equity path set is full (${max}).`}
          />
        ))}
      </SelectionCompareProvider>
    );

    const toggle = screen.getByRole("button", { name: "Chart equity path for One" });
    expect(toggle).toHaveTextContent(/^▢$/);
    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: "Chart equity path for Two" }));

    expect(screen.getByRole("button", { name: "Chart equity path for One" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Remove One from the equity path set" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Chart 2 equity paths" }));

    expect(screen.getByRole("region", { name: "Equity paths on one scale" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close equity path chart" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /compare/i })).not.toBeInTheDocument();
  });

  it("gives parts outside a provider their own private set", async () => {
    const user = userEvent.setup();
    render(
      <>
        <CompareToggle item={items[0]} />
        <CompareTray emptyLabel="Nothing yet." />
      </>
    );

    await user.click(screen.getByRole("button", { name: "Add to comparison: One" }));

    expect(screen.getByRole("button", { name: "Remove from comparison: One" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("region", { name: "Comparison set" })).toHaveTextContent("0 of 6");
  });

  it("has no basic accessibility violations while building, full and open", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <Harness
        provider={{ max: 2 }}
        tray={{ emptyLabel: "Tick two rows.", label: "runs to compare" }}
        sheet={{
          footerLabel: "Total return",
          footerValue: () => "+1.00%",
          basis: "Rebased to 100.",
          seriesFor: (item) => item.id === "two"
            ? { status: "unavailable", reason: "No series." }
            : undefined
        }}
      />
    );

    expect((await axe(container)).violations).toEqual([]);

    await user.click(toggleFor("One"));
    await user.click(toggleFor("Two"));
    expect((await axe(container)).violations).toEqual([]);

    await user.click(screen.getByRole("button", { name: "Compare 2" }));
    await user.click(screen.getByText("View plotted values"));
    expect(screen.getByRole("region", { name: "Comparison by selection" })).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
