import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import {
  ChartAccountPathBuilder,
  ConfigureKeyValueField
} from "@/screens/accounting-screen.configure-panel";
import type { AccountingChartAccountEditorViewModel } from "@/screens/accounting-screen.view-model";

function ControlledKeyValue({ initial, onSerialized }: { initial: string; onSerialized: (value: string) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <ConfigureKeyValueField
      id="test-map"
      label="Account mappings"
      value={value}
      onChange={(next) => {
        setValue(next);
        onSerialized(next);
      }}
      addLabel="Add mapping"
    />
  );
}

describe("ConfigureKeyValueField", () => {
  it("renders existing key=value rows as editable inputs", () => {
    render(<ControlledKeyValue initial={"fundId=fund-alpha\nbookId=book-primary"} onSerialized={() => undefined} />);
    expect(screen.getByDisplayValue("fundId")).toBeInTheDocument();
    expect(screen.getByDisplayValue("fund-alpha")).toBeInTheDocument();
    expect(screen.getByDisplayValue("book-primary")).toBeInTheDocument();
  });

  it("adds a new row and serializes edits back to key=value text", () => {
    const onSerialized = vi.fn();
    render(<ControlledKeyValue initial="" onSerialized={onSerialized} />);
    fireEvent.click(screen.getByRole("button", { name: "Add mapping" }));
    fireEvent.change(screen.getByLabelText("Account mappings key 1"), { target: { value: "costCenter" } });
    fireEvent.change(screen.getByLabelText("Account mappings value 1"), { target: { value: "fund-accounting" } });
    expect(onSerialized).toHaveBeenLastCalledWith("costCenter=fund-accounting");
  });

  it("removes a row", () => {
    const onSerialized = vi.fn();
    render(<ControlledKeyValue initial={"a=1\nb=2"} onSerialized={onSerialized} />);
    fireEvent.click(screen.getByRole("button", { name: "Remove Account mappings row 1" }));
    expect(onSerialized).toHaveBeenLastCalledWith("b=2");
  });
});

function makeChartEditor(overrides: Partial<AccountingChartAccountEditorViewModel> = {}): AccountingChartAccountEditorViewModel {
  return {
    nodeIdValue: "",
    pathValue: "1000.Assets",
    accountNameValue: "",
    accountTypeValue: "",
    parentPathValue: "",
    financialAccountIdValue: "",
    evidenceValue: "",
    saveButtonLabel: "Save",
    saveDisabledReason: null,
    statusText: null,
    saveBusy: false,
    canSave: true,
    updateDraft: vi.fn(),
    save: vi.fn(),
    ...overrides
  };
}

describe("ChartAccountPathBuilder", () => {
  it("shows breadcrumb segments and sets the path when a segment is clicked", () => {
    const updateDraft = vi.fn();
    render(<ChartAccountPathBuilder editor={makeChartEditor({ updateDraft })} />);
    const breadcrumb = screen.getByLabelText("Current chart path breadcrumb");
    fireEvent.click(within(breadcrumb).getByRole("button", { name: /Assets/ }));
    expect(updateDraft).toHaveBeenCalledWith({ path: "1000.Assets" });
  });

  it("promotes the current path to the parent for the next child", () => {
    const updateDraft = vi.fn();
    render(<ChartAccountPathBuilder editor={makeChartEditor({ updateDraft })} />);
    fireEvent.click(screen.getByRole("button", { name: "Use as parent for next child" }));
    expect(updateDraft).toHaveBeenCalledWith({ parentPath: "1000.Assets", path: "" });
  });

  it("appends a typed segment using the existing separator", () => {
    const updateDraft = vi.fn();
    render(<ChartAccountPathBuilder editor={makeChartEditor({ updateDraft })} />);
    fireEvent.change(screen.getByLabelText("New chart path segment"), { target: { value: "Cash" } });
    fireEvent.click(screen.getByRole("button", { name: "Append" }));
    expect(updateDraft).toHaveBeenCalledWith({ path: "1000.Assets.Cash" });
  });
});
