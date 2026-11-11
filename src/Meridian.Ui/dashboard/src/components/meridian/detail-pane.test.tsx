import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DetailFieldItem, DetailFields, DetailPane } from "@/components/meridian/detail-pane";

const orderFields = [
  { label: "Symbol", value: "MSFT" },
  { label: "Quantity", value: "20" },
  { label: "Submitted", value: "09:42:00 ET", tone: "muted" as const },
  { label: "Provider", value: "Interactive Brokers", tone: "muted" as const },
  { label: "Venue order ID", value: "0f8fad5b-d9cb-469f-a165-70867728950e" }
];

describe("DetailPane", () => {
  it("states the object type, name and one status in a single heading line", () => {
    render(
      <DetailPane
        id="order-detail"
        ariaLabel="Order detail for PO-1"
        objectType="Order"
        title="PO-1"
        status={{ label: "Working", tone: "success" }}
        subtitle="Buy 20 MSFT"
        fields={orderFields}
      />
    );

    const pane = screen.getByRole("region", { name: "Order detail for PO-1" });
    expect(within(pane).getByRole("heading", { level: 3, name: "Order PO-1" })).toBeInTheDocument();
    expect(within(pane).getByText("Working")).toBeInTheDocument();
    expect(within(pane).getByText("Buy 20 MSFT")).toBeInTheDocument();
    // The pane names its selection itself: no separate source pill or "Detail" chip.
    expect(pane).toHaveAttribute("data-detail-pane", "true");
    expect(pane).not.toHaveAttribute("data-selected-source");
    expect(within(pane).queryByText("Detail")).not.toBeInTheDocument();
  });

  it("keeps values left-aligned and whole: prose wraps at words, figures and dates stay on one line", () => {
    render(<DetailPane id="order-detail" ariaLabel="Order detail" objectType="Order" title="PO-1" fields={orderFields} />);

    const provider = screen.getByText("Interactive Brokers");
    expect(provider).toHaveClass("text-left", "break-words", "hyphens-none");
    expect(provider).not.toHaveClass("font-mono", "text-right");
    expect(screen.getByText("20")).toHaveClass("whitespace-nowrap", "font-mono", "tabular-nums");
    expect(screen.getByText("09:42:00 ET")).toHaveClass("whitespace-nowrap");
    expect(screen.getByText("09:42:00 ET")).toHaveAttribute("data-detail-kind", "date");
  });

  it("does not repeat the columns the selected row already shows", () => {
    render(
      <DetailPane
        id="order-detail"
        ariaLabel="Order detail"
        objectType="Order"
        title="PO-1"
        fields={orderFields}
        shownInRow={["Symbol", "Submitted"]}
      />
    );

    const list = screen.getByLabelText("Order details");
    expect(within(list).queryByText("MSFT")).not.toBeInTheDocument();
    expect(within(list).queryByText("09:42:00 ET")).not.toBeInTheDocument();
    expect(within(list).getByText("Interactive Brokers")).toBeInTheDocument();
  });

  it("moves opaque identifiers into a copyable Technical details disclosure", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    render(<DetailPane id="order-detail" ariaLabel="Order detail" objectType="Order" title="PO-1" fields={orderFields} />);

    const identifier = screen.getByText("0f8fad5b-d9cb-469f-a165-70867728950e");
    expect(identifier.closest("details")).not.toBeNull();
    expect(within(identifier.closest("details") as HTMLElement).getByText("Technical details")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Copy Venue order ID", hidden: true }));

    expect(writeText).toHaveBeenCalledWith("0f8fad5b-d9cb-469f-a165-70867728950e");
    expect(screen.getByRole("button", { name: "Venue order ID copied", hidden: true })).toBeInTheDocument();
  });

  it("shows the next action and any notice with the selection", () => {
    render(
      <DetailPane
        id="order-detail"
        ariaLabel="Order detail"
        objectType="Order"
        title="PO-1"
        actions={<button type="button">Cancel order</button>}
        notice={<p>Selected order is outside the current results.</p>}
      />
    );

    expect(screen.getByRole("button", { name: "Cancel order" })).toBeInTheDocument();
    expect(screen.getByText("Selected order is outside the current results.")).toBeInTheDocument();
  });

  it("shows the empty state instead of a heading when nothing is selected", () => {
    render(
      <DetailPane
        id="order-detail"
        ariaLabel="Trading blotter detail"
        objectType="Order"
        empty={{ title: "No order selected", text: "No open orders require operator action." }}
      />
    );

    expect(screen.getByRole("status")).toHaveTextContent("No order selectedNo open orders require operator action.");
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
  });
});

describe("DetailFields", () => {
  it("renders nothing for an empty list", () => {
    const { container } = render(<DetailFields fields={[{ label: "Note", value: "" }]} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("flows into columns in a wide surface when asked", () => {
    render(<DetailFields fields={orderFields} label="Order facts" columns="auto" />);

    expect(screen.getByLabelText("Order facts").className).toContain("auto-fit");
  });
});

describe("DetailFieldItem", () => {
  it("lets the value wrap under its label instead of shrinking below a readable width", () => {
    render(<dl><DetailFieldItem label="Provider" value="Interactive Brokers" tone="warning" /></dl>);

    const value = screen.getByText("Interactive Brokers");
    expect(value.parentElement).toHaveClass("flex", "flex-wrap");
    expect(value).toHaveClass("grow", "basis-[9.5rem]", "text-warning");
    expect(screen.getByText("Provider")).toHaveClass("min-w-[7rem]", "shrink-0");
  });
});
