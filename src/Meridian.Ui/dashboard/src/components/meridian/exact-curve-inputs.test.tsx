import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { listSecurityCurves, listSecurityCurveVersions } from "@/lib/api/security-curves.api";
import type { ExactCurvePin } from "@/types/security-curves.types";
import { ExactCurveInputs } from "./exact-curve-inputs";

vi.mock("@/lib/api/security-curves.api", () => ({ listSecurityCurves: vi.fn(), listSecurityCurveVersions: vi.fn() }));
const definition = { curveId: "curve-1", curveKey: "SOFR", kind: "Forward", currency: "USD" };
const version = { curveId: "curve-1", curveVersionId: "version-1", version: 1, asOf: "2026-11-01", recordedAt: "2026-11-01T00:00:00Z", contentSha256: "exact-hash" };
function Harness() {
  const [pins, setPins] = useState<ExactCurvePin[]>([]);
  return <><ExactCurveInputs pins={pins} onChange={setPins} /><output data-testid="request">{JSON.stringify(pins)}</output></>;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listSecurityCurves).mockResolvedValue([definition] as never); vi.mocked(listSecurityCurveVersions).mockResolvedValue([version] as never); });
describe("exact curve input selection", () => {
  it("requires explicit retained selection, exposes its hash and clears the version when the curve changes", async () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Add curve pin" }));
    await waitFor(() => expect(screen.getByLabelText("Curve 1")).toHaveTextContent("SOFR"));
    fireEvent.change(screen.getByLabelText("Calculation role 1"), { target: { value: "SOFR" } });
    fireEvent.change(screen.getByLabelText("Curve 1"), { target: { value: "curve-1" } });
    await waitFor(() => expect(screen.getByLabelText("Curve version 1")).toHaveTextContent("v1"));
    expect(screen.getByLabelText("Curve version 1")).toHaveValue("");
    fireEvent.change(screen.getByLabelText("Curve version 1"), { target: { value: "version-1" } });
    expect(screen.getByText(/SHA-256 exact-hash/)).toBeInTheDocument();
    expect(screen.getByTestId("request")).toHaveTextContent('"curveVersionId":"version-1"');
    fireEvent.change(screen.getByLabelText("Curve 1"), { target: { value: "" } });
    expect(screen.getByTestId("request")).toHaveTextContent('"curveVersionId":""');
    expect(screen.getByLabelText("Curve version 1")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Remove curve pin 1" }));
    expect(screen.getByTestId("request")).toHaveTextContent("[]");
  });
  it("rejects late versions from a deselected curve", async () => {
    let finish: (rows: never) => void;
    vi.mocked(listSecurityCurveVersions).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Add curve pin" }));
    await waitFor(() => expect(screen.getByLabelText("Curve 1")).toHaveTextContent("SOFR"));
    fireEvent.change(screen.getByLabelText("Curve 1"), { target: { value: "curve-1" } });
    fireEvent.change(screen.getByLabelText("Curve 1"), { target: { value: "" } });
    await act(async () => finish([version] as never));
    expect(screen.getByLabelText("Curve version 1")).not.toHaveTextContent("Loading");
    expect(screen.getByLabelText("Curve version 1")).not.toHaveTextContent("v1");
  });
  it("shows unavailable definitions and provides an explicit retry", async () => {
    vi.mocked(listSecurityCurves).mockRejectedValueOnce(new Error("Curve service offline"));
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Add curve pin" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Curve service offline");
    fireEvent.click(screen.getByRole("button", { name: "Retry curves" }));
    await waitFor(() => expect(screen.getByLabelText("Curve 1")).toHaveTextContent("SOFR"));
  });
});
