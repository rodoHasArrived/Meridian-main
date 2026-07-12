import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FOCUS_SIGNAL_LIMIT, ScreenLayout, type FocusSignal } from "@/components/ui/screen-layout";

describe("ScreenLayout", () => {
  it("renders the header zone with title, scope, and top-right actions", () => {
    render(
      <ScreenLayout
        title="Trial Balance"
        scope="All entities · Primary GL"
        actions={<button type="button">Export</button>}
      >
        <div>work</div>
      </ScreenLayout>
    );

    // The app shell owns the page <h1>; a screen title is an <h2> beneath it.
    const heading = screen.getByRole("heading", { level: 2, name: "Trial Balance" });
    expect(heading).toBeInTheDocument();
    expect(screen.getByText("All entities · Primary GL")).toBeInTheDocument();

    // Primary actions live in the header's banner, always top-right.
    const header = screen.getByRole("banner");
    expect(within(header).getByRole("button", { name: "Export" })).toBeInTheDocument();
  });

  it("renders focus signals as an opinionated row and caps them at the limit", () => {
    const signals: FocusSignal[] = Array.from({ length: FOCUS_SIGNAL_LIMIT + 2 }, (_, index) => ({
      id: `s${index}`,
      label: `Label ${index}`,
      value: `Value ${index}`
    }));

    render(
      <ScreenLayout title="Screen" focus={signals}>
        <div>work</div>
      </ScreenLayout>
    );

    expect(screen.getByText(`Value ${FOCUS_SIGNAL_LIMIT - 1}`)).toBeInTheDocument();
    // Signals beyond the limit are dropped rather than becoming a metric wall.
    expect(screen.queryByText(`Value ${FOCUS_SIGNAL_LIMIT}`)).not.toBeInTheDocument();
  });

  it("collapses and expands the focus zone", async () => {
    const user = userEvent.setup();
    render(
      <ScreenLayout title="Screen" focus={<div>focus body</div>}>
        <div>work</div>
      </ScreenLayout>
    );

    const toggle = screen.getByRole("button", { name: /hide/i });
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    await user.click(toggle);

    const collapsedToggle = screen.getByRole("button", { name: /show/i });
    expect(collapsedToggle).toHaveAttribute("aria-expanded", "false");
    const region = document.getElementById(collapsedToggle.getAttribute("aria-controls") ?? "");
    expect(region).not.toBeNull();
    expect(region).toHaveAttribute("hidden");
  });

  it("starts collapsed when requested and hides the toggle when not collapsible", () => {
    const { rerender } = render(
      <ScreenLayout title="Screen" focus={<div>focus body</div>} focusDefaultCollapsed>
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.getByRole("button", { name: /show/i })).toHaveAttribute("aria-expanded", "false");

    rerender(
      <ScreenLayout title="Screen" focus={<div>focus body</div>} focusCollapsible={false}>
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.queryByRole("button", { name: /show|hide/i })).not.toBeInTheDocument();
  });

  it("omits the focus zone entirely when there are no signals", () => {
    render(
      <ScreenLayout title="Screen" focus={[]}>
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.queryByRole("region", { name: "Focus" })).not.toBeInTheDocument();
  });

  it.each([
    ["false", false],
    ["empty string", ""]
  ])("does not render empty focus/context chrome when a zone resolves to %s", (_label, value) => {
    // `focus`/`context` are ReactNode, so `cond && <X/>` can pass `false`.
    render(
      <ScreenLayout title="Screen" focus={value} context={value} contextOpen contextLabel="Detail">
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.queryByRole("region", { name: "Focus" })).not.toBeInTheDocument();
    expect(screen.queryByRole("complementary", { name: "Detail" })).not.toBeInTheDocument();
  });

  it("renders the work zone children and stacks them with the default rhythm", () => {
    render(
      <ScreenLayout title="Screen">
        <table aria-label="ledger" />
      </ScreenLayout>
    );
    const table = screen.getByRole("table", { name: "ledger" });
    expect(table).toBeInTheDocument();
    // The work container owns inter-card spacing so screens don't re-wrap content.
    expect(table.parentElement).toHaveClass("space-y-4");
  });

  it("drops the default spacing when workClassName opts into a grid/flex layout", () => {
    // `space-y-4` is a sibling-margin utility; keeping it under a grid/flex
    // misaligns rows, so a layout override must own spacing instead.
    render(
      <ScreenLayout title="Screen" workClassName="grid grid-cols-2">
        <table aria-label="ledger" />
      </ScreenLayout>
    );
    const container = screen.getByRole("table", { name: "ledger" }).parentElement;
    expect(container).toHaveClass("grid", "grid-cols-2");
    expect(container).not.toHaveClass("space-y-4");
  });

  it("keeps the default spacing for a non-layout workClassName", () => {
    render(
      <ScreenLayout title="Screen" workClassName="pt-2">
        <table aria-label="ledger" />
      </ScreenLayout>
    );
    const container = screen.getByRole("table", { name: "ledger" }).parentElement;
    expect(container).toHaveClass("space-y-4", "pt-2");
  });

  it("shows the context rail only when open, and closes on request", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    const { rerender } = render(
      <ScreenLayout
        title="Screen"
        context={<div>evidence</div>}
        contextOpen={false}
        contextLabel="Account detail"
        onContextClose={onClose}
      >
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.queryByRole("complementary", { name: "Account detail" })).not.toBeInTheDocument();

    rerender(
      <ScreenLayout
        title="Screen"
        context={<div>evidence</div>}
        contextOpen
        contextLabel="Account detail"
        onContextClose={onClose}
      >
        <div>work</div>
      </ScreenLayout>
    );

    const rail = screen.getByRole("complementary", { name: "Account detail" });
    expect(within(rail).getByText("evidence")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Close Account detail" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("hides the close control when no handler is provided", () => {
    render(
      <ScreenLayout title="Screen" context={<div>evidence</div>} contextOpen contextLabel="Detail">
        <div>work</div>
      </ScreenLayout>
    );
    expect(screen.queryByRole("button", { name: /close detail/i })).not.toBeInTheDocument();
  });
});
