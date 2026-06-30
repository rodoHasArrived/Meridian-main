import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { Breadcrumb } from "@/components/ui/breadcrumb";
import { Checkbox, Toggle } from "@/components/ui/checkbox";
import { ContextMenu } from "@/components/ui/context-menu";
import { FormGrid, FormRow, FormSectionLabel } from "@/components/ui/form";
import { MultiSelect } from "@/components/ui/multi-select";
import { PanelSurface } from "@/components/ui/panel-surface";
import { StatusBanner } from "@/components/ui/status-banner";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { ToastProvider, useToast } from "@/components/ui/toast";

describe("design-system UI primitives", () => {
  it("renders breadcrumb and panel primitives with tokenized surfaces", () => {
    render(
      <PanelSurface aria-label="Security panel">
        <Breadcrumb items={[{ label: "Accounting", href: "/workstation/accounting" }, { label: "Security Master" }]} />
      </PanelSurface>
    );

    expect(screen.getByLabelText("Security panel")).toHaveClass("rounded-[var(--radius-card,0.5rem)]");
    expect(screen.getByText("Security Master")).toHaveAttribute("aria-current", "page");
  });

  it("toggles checkbox and switch primitives", async () => {
    const user = userEvent.setup();
    const onCheckboxChange = vi.fn();
    const onToggleChange = vi.fn();

    render(
      <>
        <Checkbox label="Include inactive" onCheckedChange={onCheckboxChange} />
        <Toggle label="Paper mode" onCheckedChange={onToggleChange} />
      </>
    );

    const checkbox = screen.getByRole("checkbox", { name: "Include inactive" });
    await user.click(checkbox);

    expect(checkbox).toBeChecked();
    expect(onCheckboxChange).toHaveBeenCalledWith(true);

    const toggle = screen.getByRole("switch", { name: "Paper mode" });
    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(onToggleChange).toHaveBeenCalledWith(true);
  });

  it("renders form, status banner, and tabs primitives", async () => {
    const user = userEvent.setup();

    render(
      <>
        <FormGrid columns={2}>
          <FormSectionLabel>Order settings</FormSectionLabel>
          <FormRow label="Limit" hint="Use ISO currency values.">
            <input aria-label="Limit" />
          </FormRow>
        </FormGrid>
        <StatusBanner tone="warning" title="Review required" detail="2 pending controls" />
        <Tabs tabs={[{ id: "open", label: "Open", count: 2 }, { id: "closed", label: "Closed" }]}>
          <TabPanel>Open panel</TabPanel>
          <TabPanel>Closed panel</TabPanel>
        </Tabs>
      </>
    );

    expect(screen.getByText("Order settings")).toBeInTheDocument();
    expect(screen.getByText("Review required")).toBeInTheDocument();
    expect(screen.getByText("Open panel")).toBeVisible();

    screen.getByRole("tab", { name: /Open/ }).focus();
    await user.keyboard("{ArrowRight}");

    expect(screen.getByText("Closed panel")).toBeVisible();
  });

  it("keeps one focusable tab while keyboard navigation skips disabled tabs", async () => {
    const user = userEvent.setup();

    render(
      <Tabs
        tabs={[
          { id: "overview", label: "Overview" },
          { id: "blocked", label: "Blocked", disabled: true },
          { id: "history", label: "History" },
          { id: "audit", label: "Audit" }
        ]}
      >
        <TabPanel>Overview panel</TabPanel>
        <TabPanel>Blocked panel</TabPanel>
        <TabPanel>History panel</TabPanel>
        <TabPanel>Audit panel</TabPanel>
      </Tabs>
    );

    const overview = screen.getByRole("tab", { name: "Overview" });
    const blocked = screen.getByRole("tab", { name: "Blocked" });
    const history = screen.getByRole("tab", { name: "History" });
    const audit = screen.getByRole("tab", { name: "Audit" });

    expect(overview).toHaveAttribute("tabindex", "0");
    expect(blocked).toBeDisabled();
    expect(history).toHaveAttribute("tabindex", "-1");
    expect(audit).toHaveAttribute("tabindex", "-1");

    overview.focus();
    await user.keyboard("{ArrowRight}");

    expect(history).toHaveFocus();
    expect(history).toHaveAttribute("aria-selected", "true");
    expect(overview).toHaveAttribute("tabindex", "-1");
    expect(blocked).toHaveAttribute("tabindex", "-1");
    expect(history).toHaveAttribute("tabindex", "0");
    expect(screen.getByText("History panel")).toBeVisible();

    await user.keyboard("{End}");
    expect(audit).toHaveFocus();
    expect(audit).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Home}");
    expect(overview).toHaveFocus();
    expect(overview).toHaveAttribute("aria-selected", "true");
  });

  it("renders context menu and multi-select command primitives", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const onClose = vi.fn();
    const onBulkAction = vi.fn();

    render(
      <>
        <ContextMenu
          open
          position={{ x: 24, y: 32 }}
          onClose={onClose}
          items={[{ id: "open", label: "Open record", onSelect }, { id: "divider", type: "divider" }, { id: "delete", label: "Delete", danger: true }]}
        />
        <MultiSelect
          selectedIds={new Set(["cash"])}
          allIds={["cash", "fees"]}
          actions={[{ id: "export", label: "Export", onSelect: onBulkAction }]}
        />
      </>
    );

    await waitFor(() => expect(screen.getByRole("menuitem", { name: "Open record" })).toHaveFocus());
    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("menuitem", { name: "Delete" })).toHaveFocus();
    await user.keyboard("{ArrowUp}");
    await user.keyboard("{Enter}");

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole("button", { name: "Export" }));

    expect(onBulkAction).toHaveBeenCalledWith(new Set(["cash"]));
  });

  it("shows and dismisses toast primitives through the provider", async () => {
    const user = userEvent.setup();

    function Harness() {
      const toast = useToast();
      return (
        <button type="button" onClick={() => toast.show({ title: "Saved", detail: "Ledger mapping updated", durationMs: 0 })}>
          Show toast
        </button>
      );
    }

    render(
      <ToastProvider>
        <Harness />
      </ToastProvider>
    );

    await user.click(screen.getByRole("button", { name: "Show toast" }));

    expect(screen.getByText("Saved")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Dismiss notification" }));

    await waitFor(() => expect(screen.queryByText("Saved")).not.toBeInTheDocument());
  });
});
