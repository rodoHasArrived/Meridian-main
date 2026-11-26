// @vitest-environment node
import { apiGetJson } from "@/lib/api";
import { getSecurityInstrumentTerms } from "./security-instrument-terms.api";
import { instrumentTermField, instrumentTermsFixture } from "@/test/security-instrument-terms-fixtures";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));

describe("getSecurityInstrumentTerms", () => {
  beforeEach(() => vi.resetAllMocks());

  it("preserves retained decimal tokens and forces an authoritative cancellable read", async () => {
    const controller = new AbortController();
    const terms = instrumentTermsFixture({ securityId: "bond /id" });
    vi.mocked(apiGetJson).mockResolvedValue(terms);
    const result = await getSecurityInstrumentTerms("bond /id", {
      signal: controller.signal,
      allowDevelopmentFallback: true,
      expectedVersion: 7,
      expectedFingerprint: "A".repeat(64)
    });
    expect(apiGetJson).toHaveBeenCalledWith("/api/security-master/bond%20%2Fid/instrument-terms", {
      signal: controller.signal,
      allowDevelopmentFallback: false
    });
    expect(result.fields[0].valueText).toBe("5.12345678901234567890");
    expect(result.fields[6].children[0].children[0].valueText).toBe("9007199254740993.123456789");
    expect(result.assetSpecificTermsJson).toBe(terms.assetSpecificTermsJson);
  });

  it("rejects foreign identities and mismatched retained versions or fingerprints", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(instrumentTermsFixture({ securityId: "foreign" }));
    await expect(getSecurityInstrumentTerms("sec-bond")).rejects.toThrow("selected security");
    vi.mocked(apiGetJson).mockResolvedValue(instrumentTermsFixture());
    await expect(getSecurityInstrumentTerms("sec-bond", { expectedVersion: 8 })).rejects.toThrow(
      "different economic-definition version"
    );
    await expect(getSecurityInstrumentTerms("sec-bond", { expectedFingerprint: "b".repeat(64) })).rejects.toThrow(
      "expected fingerprint"
    );
  });

  it("retains explicit missing, null, invalid, and ambiguous field assessments", async () => {
    const terms = instrumentTermsFixture();
    terms.fields.push(
      instrumentTermField({
        path: "/assetSpecificTerms/coupon",
        key: "coupon",
        state: "Ambiguous",
        validationMessage: "Multiple retained aliases disagree."
      })
    );
    vi.mocked(apiGetJson).mockResolvedValue(terms);
    expect((await getSecurityInstrumentTerms("sec-bond")).fields.map((field) => field.state)).toEqual(
      terms.fields.map((field) => field.state)
    );
  });

  it("preserves a retained asset-class alias separately from its canonical schema name", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(instrumentTermsFixture({ assetClass: "CorporateBond" }));
    const terms = await getSecurityInstrumentTerms("sec-bond");
    expect(terms.assetClass).toBe("CorporateBond");
    expect(terms.schema.assetClass).toBe("Bond");
  });

  it("withholds malformed transport data and preserves backend read failures", async () => {
    const terms = instrumentTermsFixture();
    vi.mocked(apiGetJson).mockResolvedValue({ ...terms, fields: [{ ...terms.fields[0], valueText: 0.5 }] });
    await expect(getSecurityInstrumentTerms("sec-bond")).rejects.toThrow("malformed");
    vi.mocked(apiGetJson).mockRejectedValue(new Error("Retained terms are unavailable"));
    await expect(getSecurityInstrumentTerms("sec-bond")).rejects.toThrow("Retained terms are unavailable");
  });
});
