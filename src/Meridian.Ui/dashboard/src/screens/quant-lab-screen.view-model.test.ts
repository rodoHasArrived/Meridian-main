import { describe, expect, it } from "vitest";
import {
  buildParameterRow,
  buildQuantParameters,
  buildRunCommandState,
  buildTemplatePanelState,
  buildToolbarItems,
  initializeNewParameterValues,
  mergeQuantParameters,
  validateQuantSource,
  type QuantRunState
} from "@/screens/quant-lab-screen.view-model";
import type { QuantParameter } from "@/types";

const numberParameter: QuantParameter = {
  name: "lookback",
  label: "Lookback",
  typeName: "int",
  defaultValue: "20",
  description: "Rolling window length",
  min: 1,
  max: 252
};

const boolParameter: QuantParameter = {
  name: "includeFees",
  label: "Include fees",
  typeName: "bool",
  defaultValue: "true",
  description: null,
  min: null,
  max: null
};

describe("Quant Lab view model helpers", () => {
  it("merges detected and runtime parameters by stable name", () => {
    const merged = mergeQuantParameters(
      [numberParameter],
      [{ ...numberParameter, label: "Updated lookback" }, boolParameter]
    );

    expect(merged).toHaveLength(2);
    expect(merged[0]?.label).toBe("Updated lookback");
    expect(merged[1]?.name).toBe("includeFees");
  });

  it("initializes only missing parameter defaults", () => {
    expect(initializeNewParameterValues({ lookback: "50" }, [numberParameter, boolParameter])).toEqual({
      lookback: "50",
      includeFees: "true"
    });
  });

  it("coerces typed parameter payloads before running scripts", () => {
    expect(buildQuantParameters([numberParameter, boolParameter], {
      lookback: "63",
      includeFees: "false"
    })).toEqual({
      lookback: 63,
      includeFees: false
    });
  });

  it("builds disabled run command state for empty source", () => {
    expect(validateQuantSource("   ")).toBe("Enter some script source first.");
    expect(buildRunCommandState("   ", "idle")).toMatchObject({
      label: "Run",
      disabled: true,
      disabledReason: "Enter some script source first.",
      busy: false
    });
  });

  it("projects loading, empty, and error template states into accessible panel copy", () => {
    expect(buildTemplatePanelState("loading", null)).toMatchObject({
      role: "status",
      ariaLive: "polite",
      message: "Loading starter templates..."
    });
    expect(buildTemplatePanelState("empty", null).message).toMatch(/No starter templates/i);
    expect(buildTemplatePanelState("error", "Templates API unavailable")).toMatchObject({
      role: "alert",
      ariaLive: "assertive",
      message: "Templates API unavailable"
    });
  });

  it("derives parameter row input metadata and reset affordance", () => {
    const row = buildParameterRow(numberParameter, { lookback: "63" });

    expect(row).toMatchObject({
      name: "lookback",
      inputType: "number",
      value: "63",
      min: 1,
      max: 252,
      step: "1",
      isDefault: false,
      resetLabel: "Reset Lookback to default"
    });
  });

  it("builds toolbar status from source, templates, parameters, and run phase", () => {
    const running: QuantRunState = { phase: "running", result: null, error: null };

    expect(buildToolbarItems("Print(\"hi\");", 2, 1, running, "ready")).toEqual([
      { id: "source", label: "Source", value: "Ready", active: true },
      { id: "templates", label: "Templates", value: "2" },
      { id: "params", label: "Params", value: "1", active: true },
      { id: "run", label: "Run", value: "running", active: true }
    ]);
  });
});
