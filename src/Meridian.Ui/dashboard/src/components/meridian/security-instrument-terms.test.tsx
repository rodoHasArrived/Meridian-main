import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { SecurityInstrumentTerms } from "./security-instrument-terms";
import { getSecurityInstrumentTerms } from "@/lib/api/security-instrument-terms.api";
import { instrumentTermField, instrumentTermsFixture } from "@/test/security-instrument-terms-fixtures";
import type { SecurityInstrumentTerms as Terms } from "@/types/security-instrument-terms";

vi.mock("@/lib/api/security-instrument-terms.api", () => ({ getSecurityInstrumentTerms: vi.fn() }));

describe("SecurityInstrumentTerms", () => {
  beforeEach(() => vi.resetAllMocks());

  it("highlights canonical option terms for an aliased retained asset class", async () => {
    const option = instrumentTermsFixture({ assetClass: "ListedDerivative", status: "Ready", fields: [
      instrumentTermField({ path: "/assetSpecificTerms/underlyingId", key: "underlyingId", label: "Underlying", valueText: "underlying-1", rawJson: '"underlying-1"', jsonType: "String", declaredType: "Guid", unit: null, unitStatus: "NotApplicable", unitPath: null }),
      instrumentTermField({ path: "/assetSpecificTerms/strike", key: "strike", label: "Strike", valueText: "200.123456789012345678" }),
      instrumentTermField({ path: "/assetSpecificTerms/expiry", key: "expiry", label: "Expiry", valueText: "2040-01-20", jsonType: "String", declaredType: "Date", unit: null, unitStatus: "NotApplicable", unitPath: null }),
      instrumentTermField({ path: "/assetSpecificTerms/multiplier", key: "multiplier", label: "Multiplier", valueText: "100", unit: "Shares per contract", unitPath: "/assetSpecificTerms/multiplierUnit" })
    ] });
    option.schema.assetClass = "Option";
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(option);
    render(<SecurityInstrumentTerms securityId="sec-bond" mode="summary" />);
    expect(await screen.findByText("underlying-1", { selector: "span" })).toBeVisible();
    expect(screen.getByText("200.123456789012345678", { selector: "span" })).toBeVisible();
    expect(screen.getByText("2040-01-20", { selector: "span" })).toBeVisible();
    expect(screen.getByText("100", { selector: "span" })).toBeVisible();
  });

  it("shows asset-specific highlights with exact decimal text and explicit units", async () => {
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(instrumentTermsFixture());
    const openFull = vi.fn();
    const user = userEvent.setup();
    render(<SecurityInstrumentTerms securityId="sec-bond" mode="summary" expectedVersion={7} onOpenFull={openFull} />);
    expect(await screen.findByRole("heading", { name: "Instrument terms" })).toBeInTheDocument();
    expect(screen.getByText("5.12345678901234567890", { selector: "span" })).toBeVisible();
    expect(screen.getByText("Unit: Percent")).toBeVisible();
    expect(screen.getByText("2040-06-30", { selector: "span" })).toBeVisible();
    expect(screen.getByText("Not supplied")).toBeVisible();
    expect(getSecurityInstrumentTerms).toHaveBeenCalledWith(
      "sec-bond",
      expect.objectContaining({ expectedVersion: 7, signal: expect.any(AbortSignal) })
    );
    await user.click(screen.getByRole("button", { name: "View all terms" }));
    expect(openFull).toHaveBeenCalledOnce();
  });

  it("keeps missing, null, invalid, undeclared and unspecified-unit values distinct", async () => {
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(instrumentTermsFixture());
    const { container } = render(<SecurityInstrumentTerms securityId="sec-bond" />);
    await screen.findByRole("heading", { name: "Full instrument terms" });
    const rowFor = (label: string) => screen.getByText(label, { exact: true }).closest("div")!;
    expect(rowFor("Payment frequency")).toHaveTextContent("Not supplied");
    expect(rowFor("Redemption amount")).toHaveTextContent("Explicit null");
    expect(rowFor("Invalid rate")).toHaveTextContent("Invalid value");
    expect(rowFor("Invalid rate")).toHaveTextContent("many");
    expect(rowFor("Invalid rate")).toHaveTextContent("Unit unspecified in retained terms");
    expect(rowFor("Vendor flag")).toHaveTextContent("Undeclared field");
    expect(rowFor("Vendor flag")).toHaveTextContent("Operational reference");
    expect(rowFor("Vendor flag")).toHaveTextContent("false");
    expect((await axe(container)).violations).toHaveLength(0);
  });

  it("searches nested schedules with their context and exposes exact retained raw blocks", async () => {
    const terms = instrumentTermsFixture();
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(terms);
    const user = userEvent.setup();
    render(<SecurityInstrumentTerms securityId="sec-bond" />);
    const search = await screen.findByRole("searchbox", { name: "Search instrument terms" });
    expect(screen.queryByText("Cash amount", { exact: true })).not.toBeInTheDocument();
    await user.type(search, "cash amount");
    expect(await screen.findByText("Cash amount", { exact: true })).toBeVisible();
    expect(screen.getByText("Coupon schedule", { exact: true })).toBeVisible();
    expect(screen.getByText("9007199254740993.123456789", { selector: "span" })).toBeVisible();
    await user.click(screen.getByText("Raw asset-specific terms", { exact: true }));
    expect(screen.getByText(terms.assetSpecificTermsJson!, { selector: "pre" })).toBeVisible();
    await user.clear(search);
    await user.click(screen.getByText("Terms provenance & schema", { exact: true }));
    expect(screen.getByText(terms.fingerprint)).toBeVisible();
    expect(screen.queryByText("Retained provider record for economic-definition v7.")).not.toBeInTheDocument();
    expect(screen.getByText(/Matched · Retained provider record/)).toBeVisible();
  });

  it("keeps unavailable terms separate from absent data and retries independently", async () => {
    vi.mocked(getSecurityInstrumentTerms)
      .mockRejectedValueOnce(new Error("Retained source unavailable"))
      .mockResolvedValueOnce(instrumentTermsFixture());
    const user = userEvent.setup();
    render(<SecurityInstrumentTerms securityId="sec-bond" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Retained source unavailable");
    expect(screen.queryByText("No instrument terms are supplied for this record.")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Retry instrument terms" }));
    expect(await screen.findByRole("heading", { name: "Full instrument terms" })).toBeInTheDocument();
  });

  it("aborts old reads and never restores terms for a previous security or version", async () => {
    let resolveFirst!: (result: Terms) => void;
    vi.mocked(getSecurityInstrumentTerms)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFirst = resolve;
        })
      )
      .mockResolvedValueOnce(instrumentTermsFixture({ securityId: "sec-other", version: 8, fields: [] }));
    const { rerender } = render(<SecurityInstrumentTerms securityId="sec-bond" expectedVersion={7} />);
    const signal = vi.mocked(getSecurityInstrumentTerms).mock.calls[0][1]?.signal;
    rerender(<SecurityInstrumentTerms securityId="sec-other" expectedVersion={8} />);
    expect(signal?.aborted).toBe(true);
    await screen.findByRole("heading", { name: "Full instrument terms" });
    expect(screen.getByText(/Economic definition v8/)).toBeVisible();
    await act(async () => {
      resolveFirst(instrumentTermsFixture());
      await Promise.resolve();
    });
    expect(screen.getByText(/Economic definition v8/)).toBeVisible();
    expect(screen.queryByText("Coupon rate", { exact: true })).not.toBeInTheDocument();
  });

  it("keeps source version mismatch visible while retaining inspectable fields", async () => {
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(
      instrumentTermsFixture({
        source: { status: "VersionMismatch", summary: "Source evidence belongs to version 6.", provenanceJson: null },
        diagnostics: ["Retained profile definition is unavailable."]
      })
    );
    render(<SecurityInstrumentTerms securityId="sec-bond" />);
    expect(await screen.findByText(/Source evidence: Version mismatch/)).toBeVisible();
    expect(screen.getByText("Retained profile definition is unavailable.")).toBeVisible();
    const group = screen.getByRole("region", { name: "Asset-specific terms instrument terms" });
    expect(within(group).getByText("Coupon rate", { exact: true })).toBeVisible();
    await waitFor(() => expect(getSecurityInstrumentTerms).toHaveBeenCalledOnce());
  });

  it("retains both conflicting occurrences of a duplicate source property", async () => {
    vi.mocked(getSecurityInstrumentTerms).mockResolvedValue(
      instrumentTermsFixture({
        fields: [
          instrumentTermField({ state: "Ambiguous", valueText: "4.0000000000000000001" }),
          instrumentTermField({ state: "Ambiguous", valueText: "5.0000000000000000002" })
        ]
      })
    );
    render(<SecurityInstrumentTerms securityId="sec-bond" />);
    expect(await screen.findByText("4.0000000000000000001", { selector: "span" })).toBeVisible();
    expect(screen.getByText("5.0000000000000000002", { selector: "span" })).toBeVisible();
    expect(screen.getAllByText("Ambiguous value")).toHaveLength(2);
  });
});
