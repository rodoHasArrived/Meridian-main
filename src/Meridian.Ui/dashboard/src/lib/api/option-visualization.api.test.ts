import { afterEach, describe, expect, it, vi } from "vitest";
import { acceptOptionAssessment, listOptionAssessments, previewOptionAssessment, screenOptions } from "./option-visualization.api";
import { DEFAULT_OPTION_CRITERIA, EMPTY_OPTION_BASKET, emptyAssessment } from "@/screens/options-screen.view-model";

describe("option visualization authoritative API", () => {
  afterEach(() => vi.unstubAllGlobals());
  it("retains exact imported amounts, preview fingerprints and idempotency", async () => {
    const fetch = vi.fn().mockImplementation(async () => new Response("{}", { status: 200 })); vi.stubGlobal("fetch", fetch);
    const request = { assessment: { ...emptyAssessment("account", EMPTY_OPTION_BASKET), initialMarginAfter: "123.1234567890123456789" },
      sourceFileName: "source.txt", sourceContentBase64: "c291cmNl", sourceContentType: "text/plain", legsCsv: null, correctsAssessmentId: null };
    await previewOptionAssessment("account", request);
    await acceptOptionAssessment("account", { idempotencyKey: "stable-key", previewFingerprint: "reviewed", import: request });
    expect(JSON.parse(fetch.mock.calls[1][1].body).import.assessment.initialMarginAfter).toBe("123.1234567890123456789");
    expect(JSON.parse(fetch.mock.calls[1][1].body).idempotencyKey).toBe("stable-key");
    expect(fetch.mock.calls[1][0]).toContain("/api/options/assessments/accounts/account/accept");
  });
  it("propagates missing provider and retained-evidence failures without fixtures", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => new Response('{"detail":"Unavailable evidence"}', { status: 503 })));
    await expect(screenOptions({ ...DEFAULT_OPTION_CRITERIA, underlyingSymbol: "XYZ" })).rejects.toMatchObject({ status: 503 });
    await expect(listOptionAssessments("account", { allowDevelopmentFallback: true })).rejects.toMatchObject({ status: 503 });
  });
});
