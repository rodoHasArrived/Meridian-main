import { describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { privateCapitalApi } from "./private-capital-records.api";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn().mockResolvedValue({}), apiPostJson: vi.fn().mockResolvedValue({}) }));

describe("private-capital record API", () => {
  it("disables development fallback and sends acceptance identities without financial amounts", async () => {
    const controller = new AbortController();
    await privateCapitalApi.records({ signal: controller.signal, allowDevelopmentFallback: true });
    expect(apiGetJson).toHaveBeenCalledWith("/api/private-capital/records", { signal: controller.signal, allowDevelopmentFallback: false });
    const command = { batchId: "batch", fingerprint: "hash", commandId: "same-command" };
    await privateCapitalApi.accept(command);
    expect(apiPostJson).toHaveBeenCalledWith("/api/private-capital/accept", command, { allowDevelopmentFallback: false });
  });
});
