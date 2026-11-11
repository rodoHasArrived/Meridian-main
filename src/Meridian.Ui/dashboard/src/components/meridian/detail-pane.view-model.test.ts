import { describe, expect, it } from "vitest";
import {
  inferDetailFieldKind,
  isDataFaceValue,
  partitionDetailFields,
  usesDataFace
} from "@/components/meridian/detail-pane.view-model";

describe("inferDetailFieldKind", () => {
  it.each([
    ["1,250", "number"],
    ["-$4,118.22", "number"],
    ["(1,234.00)", "number"],
    ["42.5%", "number"],
    ["+3.1 bps", "number"],
    ["500.00 USD", "number"],
    ["+$90", "number"],
    ["2026-06-30", "date"],
    ["2026-06-30T18:05:00Z", "date"],
    ["09:42:00 ET", "date"],
    ["Jan 1, 2026, 00:00 UTC", "date"],
    ["Jun 30, 2026", "date"],
    ["2s ago", "date"],
    ["PO-1", "code"],
    ["brk-2026-0042", "code"],
    ["ACC_1001", "code"],
    ["0f8fad5b-d9cb-469f-a165-70867728950e", "id"],
    ["sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", "id"],
    ["Interactive Brokers", "text"],
    ["Review required", "text"],
    ["AAPL", "text"],
    ["3 breaks", "text"],
    ["—", "text"]
  ])("reads %s as %s", (value, kind) => {
    expect(inferDetailFieldKind(value)).toBe(kind);
  });
});

describe("data face", () => {
  it("keeps figures, identifiers and numeric dates in the data face", () => {
    for (const value of ["1,234", "-$512.00", "(500.00)", "12.5%", "+0.25", "500.00 USD", "je-cash-1", "2026-06-30", "ACC_1001", "09:42:00 ET"]) {
      expect(isDataFaceValue(value), value).toBe(true);
    }
  });

  it("sets phrases, statuses and written-out dates in the body face", () => {
    for (const value of ["Posted", "Not retained", "Jun 30, 2026, 18:05 UTC", "Cash sweep", "Interactive Brokers", "", "—"]) {
      expect(isDataFaceValue(value), value).toBe(false);
    }
  });

  it("decides a date by whether it starts with a digit", () => {
    expect(usesDataFace("date", "2026-06-30")).toBe(true);
    expect(usesDataFace("date", "Jun 30, 2026")).toBe(false);
    expect(usesDataFace("text", "2026 plan")).toBe(false);
  });
});

describe("partitionDetailFields", () => {
  const fields = [
    { label: "Symbol", value: "MSFT" },
    { label: "Quantity", value: "20" },
    { label: "Provider", value: "Interactive Brokers", tone: "muted" as const },
    { label: "Record ID", value: "0f8fad5b-d9cb-469f-a165-70867728950e" },
    { label: "Routing key", value: "ib-paper", technical: true },
    { label: "Note", value: "  " }
  ];

  it("resolves a kind and tone for each row and drops empty values", () => {
    const { visible } = partitionDetailFields(fields);

    expect(visible).toEqual([
      { label: "Symbol", value: "MSFT", kind: "text", tone: "default" },
      { label: "Quantity", value: "20", kind: "number", tone: "default" },
      { label: "Provider", value: "Interactive Brokers", kind: "text", tone: "muted" }
    ]);
  });

  it("moves opaque identifiers and rows marked technical out of the reading path", () => {
    const { technical } = partitionDetailFields(fields);

    expect(technical.map((field) => field.label)).toEqual(["Record ID", "Routing key"]);
  });

  it("does not repeat what the selected row already shows, whatever the label's case", () => {
    const { visible, technical } = partitionDetailFields(fields, { shownInRow: ["symbol", " Quantity "] });

    expect(visible.map((field) => field.label)).toEqual(["Provider"]);
    // Identifiers stay available even when a column of the same name exists.
    expect(partitionDetailFields(fields, { shownInRow: ["Record ID"] }).technical.map((field) => field.label))
      .toEqual(technical.map((field) => field.label));
  });

  it("honours an explicit kind over the inferred one", () => {
    const { visible } = partitionDetailFields([{ label: "Lot", value: "2026-06-30", kind: "code" }]);

    expect(visible[0]?.kind).toBe("code");
  });
});
