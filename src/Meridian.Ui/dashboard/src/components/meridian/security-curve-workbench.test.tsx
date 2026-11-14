import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { ApiError } from "@/lib/api-errors";
import { defineSecurityCurve, listSecurityCurves, listSecurityCurveVersions, publishSecurityCurve } from "@/lib/api/security-curves.api";
import { SecurityCurveWorkbench } from "./security-curve-workbench";
import type { SecurityCurveDefinition, SecurityCurveVersion } from "@/types/security-curves.types";

vi.mock("@/lib/api/security-curves.api", () => ({ defineSecurityCurve: vi.fn(), listSecurityCurves: vi.fn(), listSecurityCurveVersions: vi.fn(), publishSecurityCurve: vi.fn() }));
const definition: SecurityCurveDefinition = { curveId: "curve-1", curveKey: "USD discount", kind: "Discount", currency: "USD", dayCount: "Actual365", interpolation: "Linear", source: "Synthetic fixture", createdBy: "operator", createdAt: "2026-10-01T00:00:00Z" };
const version = (number: number, value: string): SecurityCurveVersion => ({ curveId: "curve-1", curveVersionId: `version-${number}`, curveKey: definition.curveKey, version: number, asOf: "2026-11-01", recordedAt: `2026-11-0${number}T00:00:00Z`, source: "Synthetic fixture", publishedBy: "operator", contentSha256: `sha-${number}`, points: [{ date: "2026-12-01", value, tenor: "1M" }, { date: "2027-01-01", value: "0.95", tenor: "2M" }] });
const change = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

beforeEach(() => { vi.clearAllMocks(); vi.mocked(listSecurityCurves).mockResolvedValue([definition]); vi.mocked(listSecurityCurveVersions).mockResolvedValue([version(1, "0.98"), version(2, "0.97")]); });

describe("retained curve workflow", () => {
  it("creates a definition, publishes multiple exact pillars, then inspects and compares retained history", async () => {
    vi.mocked(defineSecurityCurve).mockResolvedValue(definition);
    vi.mocked(publishSecurityCurve).mockResolvedValue(version(3, "0.960000000000000001"));
    vi.mocked(listSecurityCurves).mockResolvedValue([]);
    const { container } = render(<SecurityCurveWorkbench />);
    await screen.findByText(/No curve definitions are retained/);
    fireEvent.click(screen.getByText("Create curve definition", { selector: "summary" }));
    change("Curve key", definition.curveKey); change("Curve currency", "usd"); change("Definition source", "Synthetic fixture");
    fireEvent.submit(screen.getByRole("form", { name: "Curve definition" }));
    await waitFor(() => expect(screen.getByLabelText("Stored curve")).toHaveValue("curve-1"));
    expect(defineSecurityCurve).toHaveBeenCalledWith(expect.objectContaining({ currency: "USD", dayCount: "Actual365", indexName: null }), expect.anything());
    await waitFor(() => expect(listSecurityCurveVersions).toHaveBeenCalled());
    fireEvent.click(screen.getByText("Publish curve version", { selector: "summary" }));
    change("Curve as-of date", "2026-11-01"); change("Publication source", "Synthetic fixture");
    change("Pillar date 1", "2026-12-01"); change("Pillar value 1", "0.960000000000000001"); change("Pillar tenor 1", "1M");
    change("Pillar date 2", "2027-01-01"); change("Pillar value 2", "0.95"); change("Pillar tenor 2", "2M");
    fireEvent.click(screen.getByLabelText(/I reviewed these pillars/));
    fireEvent.submit(screen.getByRole("form", { name: "Curve version publication" }));
    await screen.findByText(/Version 3 retained/);
    expect(publishSecurityCurve).toHaveBeenCalledWith(expect.objectContaining({ curveId: "curve-1", points: [{ date: "2026-12-01", value: "0.960000000000000001", tenor: "1M" }, { date: "2027-01-01", value: "0.95", tenor: "2M" }] }), expect.anything());
    change("Compare version", "version-1");
    expect(screen.getByRole("table", { name: "Retained curve pillars" })).toHaveTextContent("0.960000000000000001");
    expect(screen.getByRole("table", { name: "Retained curve pillars" })).toHaveTextContent("0.98");
    expect(screen.getByText("SHA-256 sha-1")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("shows unavailable storage and server refusal, and prevents further unauthorized writes", async () => {
    vi.mocked(defineSecurityCurve).mockRejectedValue(new ApiError({ path: "curves", status: 403 }));
    render(<SecurityCurveWorkbench />);
    await waitFor(() => expect(listSecurityCurves).toHaveBeenCalled());
    fireEvent.click(screen.getByText("Create curve definition", { selector: "summary" }));
    change("Curve key", "USD discount"); change("Curve currency", "USD"); change("Definition source", "Synthetic");
    fireEvent.submit(screen.getByRole("form", { name: "Curve definition" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("ModifySecurityMaster permission");
    expect(screen.getByRole("button", { name: "Create curve definition" })).toBeDisabled();
  });
  it("ignores late curve history after the selected curve changes", async () => {
    let finish: (rows: SecurityCurveVersion[]) => void;
    vi.mocked(listSecurityCurveVersions).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
    render(<SecurityCurveWorkbench />);
    await waitFor(() => expect(screen.getByLabelText("Stored curve")).toHaveTextContent("USD discount"));
    change("Stored curve", "curve-1");
    change("Stored curve", "");
    await act(async () => finish([version(1, "0.98")]));
    expect(screen.queryByLabelText("Inspect version")).not.toBeInTheDocument();
    expect(listSecurityCurveVersions).toHaveBeenCalledWith("curve-1", expect.objectContaining({ signal: expect.objectContaining({ aborted: true }) }));
  });
  it("exposes a clear unavailable state and refresh when storage is absent", async () => {
    vi.mocked(listSecurityCurves).mockRejectedValueOnce(new ApiError({ path: "curves", status: 404, detail: "Curve store unavailable" }));
    render(<SecurityCurveWorkbench />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Curve store unavailable");
    fireEvent.click(screen.getByRole("button", { name: "Refresh curves" }));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });
});
