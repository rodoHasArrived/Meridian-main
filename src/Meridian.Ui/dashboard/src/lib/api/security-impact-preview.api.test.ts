import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { createSecurityImpactPreview, getSecurityImpactPreview } from "./security-impact-preview.api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const receipt = () => ({ contentSha256: "a".repeat(64), dependencyFingerprint: "b".repeat(64),
  securityId: "security-1", revisionId: "revision-1", canonicalVersion: 12,
  books: { completeness: "Unavailable" }, reports: { completeness: "Unavailable" }, periods: [], issues: [] });
beforeEach(() => vi.resetAllMocks());
describe("retained impact evidence", () => {
  it("reads authoritative evidence with development fallback disabled", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(receipt());
    await getSecurityImpactPreview("a".repeat(64));
    expect(apiGetJson).toHaveBeenCalledWith(`/api/security-master/impact-previews/${"a".repeat(64)}`,
      expect.objectContaining({ allowDevelopmentFallback: false }));
  });
  it("rejects another receipt identity", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(receipt());
    await expect(getSecurityImpactPreview("c".repeat(64))).rejects.toThrow("identity");
  });
  it("rejects a created preview belonging to another revision", async () => {
    vi.mocked(apiPostJson).mockResolvedValue(receipt());
    await expect(createSecurityImpactPreview("security-1", "revision-2", "2026-11-01T00:00:00Z")).rejects.toThrow("another proposal");
  });
  it("requires a new editor preview to retain the frozen draft fund", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ ...receipt(), fundProfileId: "another-fund" });
    await expect(createSecurityImpactPreview("security-1", "revision-1", "2026-11-01T00:00:00Z", "reviewed-fund")).rejects.toThrow("retained fund profile");
    vi.mocked(apiPostJson).mockResolvedValue({ ...receipt(), fundProfileId: "reviewed-fund" });
    await expect(createSecurityImpactPreview("security-1", "revision-1", "2026-11-01T00:00:00Z", "reviewed-fund")).resolves.toMatchObject({ fundProfileId: "reviewed-fund" });
  });
});
