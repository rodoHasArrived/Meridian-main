import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DesignSystemWorkbench } from "./design-system-workbench";

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="workbench-location">{location.pathname}{location.search}</output>;
}

function renderWorkbench(search = "") {
  return render(
    <MemoryRouter initialEntries={[`/__design-system${search}`]}>
      <DesignSystemWorkbench /><LocationProbe />
    </MemoryRouter>
  );
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
  document.body.removeAttribute("data-theme-density");
});

describe("native component workbench", () => {
  it("renders signed, zero and semantic native records without network or persistence", async () => {
    const fetch = vi.spyOn(globalThis, "fetch");
    const write = vi.spyOn(Storage.prototype, "setItem");
    const remove = vi.spyOn(Storage.prototype, "removeItem");
    renderWorkbench();

    expect(screen.getByRole("heading", { name: "Meridian native component workbench" })).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Synthetic records" });
    for (const amount of ["+$1,250.00", "-$500.00", "$0.00"]) expect(within(table).getByText(amount)).toBeInTheDocument();
    expect(within(table).getByRole("row", { name: "Select Positive sample" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("textbox", { name: "Fixture source" })).toHaveAttribute("readonly");
    await userEvent.click(screen.getByRole("button", { name: "Primary preview" }));
    expect(screen.getByText("Primary preview completed locally. No data was sent.")).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(write).not.toHaveBeenCalled();
    expect(remove).not.toHaveBeenCalled();
  });

  it("updates URL-backed controls without reading or writing operator storage, and resets local specimens", async () => {
    localStorage.setItem("meridian.workstation.appearance.v1", "system");
    localStorage.setItem("meridian.workstation.density.v1", "spacious");
    const read = vi.spyOn(Storage.prototype, "getItem");
    const write = vi.spyOn(Storage.prototype, "setItem");
    renderWorkbench();
    await userEvent.click(within(screen.getByRole("radiogroup", { name: "Appearance" })).getByRole("radio", { name: "Dark" }));
    await userEvent.click(within(screen.getByRole("radiogroup", { name: "Display density" })).getByRole("radio", { name: "Compact" }));
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Specimen surface" }), "light");
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Button state" }), "disabled");
    expect(document.documentElement).toHaveAttribute("data-theme", "dark");
    expect(document.body).toHaveAttribute("data-theme-density", "compact");
    expect(document.querySelector('[data-workbench-surface="light"]')).toHaveAttribute("data-appearance", "light");
    expect(screen.getByTestId("workbench-location")).toHaveTextContent("theme=dark&density=compact&surface=light&buttons=disabled");
    expect(screen.getByRole("button", { name: "Primary preview" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Primary preview" })).toHaveAttribute("title", "This synthetic preview is disabled.");
    await userEvent.clear(screen.getByRole("textbox", { name: "Sample reference" }));
    await userEvent.click(screen.getByRole("button", { name: "Reset workbench" }));
    expect(screen.getByTestId("workbench-location")).toHaveTextContent(/^\/__design-system$/);
    expect(document.documentElement).toHaveAttribute("data-theme", "light");
    expect(document.body).not.toHaveAttribute("data-theme-density");
    expect(screen.getByRole("textbox", { name: "Sample reference" })).toHaveValue("Synthetic reference");
    expect(screen.getByRole("button", { name: "Primary preview" })).toBeEnabled();
    expect(read).not.toHaveBeenCalled();
    expect(write).not.toHaveBeenCalled();
    read.mockRestore();
    expect(localStorage.getItem("meridian.workstation.appearance.v1")).toBe("system");
    expect(localStorage.getItem("meridian.workstation.density.v1")).toBe("spacious");
  });

  it("shows initial loading, empty and retryable error without stale inspector values", async () => {
    renderWorkbench("?data=loading");
    expect(document.querySelector('[data-workbench-data-state="loading"]')).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByRole("table", { name: "Synthetic records" })).not.toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Synthetic record inspector" })).queryByText("+$1,250.00")).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Data state" }), "empty");
    expect(within(screen.getByRole("table", { name: "Synthetic records" })).getByText("No synthetic records.")).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Data state" }), "error");
    expect(screen.getByRole("alert")).toHaveTextContent("Synthetic records unavailable");
    await userEvent.click(screen.getByRole("button", { name: "Retry synthetic records" }));
    expect(screen.getByRole("row", { name: "Select Positive sample" })).toBeInTheDocument();
    expect(screen.getByTestId("workbench-location")).toHaveTextContent(/^\/__design-system$/);
  });

  it("exposes busy semantics and blocks activation until normal state is restored", async () => {
    renderWorkbench("?buttons=busy");
    const primary = screen.getByRole("button", { name: "Primary preview" });
    expect(primary).toBeDisabled();
    expect(primary).toHaveAttribute("aria-busy", "true");
    expect(primary).toHaveTextContent("Running preview");
    await userEvent.click(primary);
    expect(screen.queryByText("Primary preview completed locally. No data was sent.")).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Button state" }), "normal");
    expect(primary).toBeEnabled();
  });

  it("links required-field validation and saves only a local message", async () => {
    const fetch = vi.spyOn(globalThis, "fetch");
    renderWorkbench();
    const reference = screen.getByRole("textbox", { name: "Sample reference" });
    await userEvent.clear(reference);
    await userEvent.click(screen.getByRole("button", { name: "Save sample reference" }));
    expect(reference).toHaveAttribute("aria-invalid", "true");
    expect(reference).toHaveAccessibleDescription("Enter a sample reference.");
    await userEvent.type(reference, "QA reference");
    await userEvent.click(screen.getByRole("button", { name: "Save sample reference" }));
    expect(reference).not.toHaveAttribute("aria-invalid");
    expect(screen.getByText("Sample reference saved locally. No data was sent.")).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("hands row activation to the inspector and restores focus on Escape", async () => {
    renderWorkbench();
    const positive = screen.getByRole("row", { name: "Select Positive sample" });
    const negative = screen.getByRole("row", { name: "Select Negative sample" });
    positive.focus();
    fireEvent.keyDown(positive, { key: "ArrowDown" });
    expect(negative).toHaveFocus();
    expect(negative).toHaveAttribute("aria-selected", "true");
    const inspector = screen.getByRole("region", { name: "Synthetic record inspector" });
    expect(within(inspector).getByRole("heading", { name: "Negative sample" })).toBeInTheDocument();
    fireEvent.keyDown(negative, { key: "Enter" });
    await waitFor(() => expect(inspector).toHaveFocus());
    fireEvent.keyDown(inspector, { key: "Escape" });
    expect(negative).toHaveFocus();
  });
});
