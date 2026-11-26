import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { apiGetJson } from "@/lib/api/http-client";
import { getSignedInUsername } from "@/lib/api/auth.api";

vi.mock("@/lib/api/http-client", () => ({ apiGetJson: vi.fn() }));

const get = vi.mocked(apiGetJson);

describe("getSignedInUsername", () => {
  beforeEach(() => get.mockReset());

  it("reads the username from /api/auth/me without a development fixture fallback", async () => {
    get.mockResolvedValueOnce({ username: "  a.chen ", role: "Operator" });
    const controller = new AbortController();
    await expect(getSignedInUsername({ signal: controller.signal })).resolves.toBe("a.chen");
    expect(get).toHaveBeenCalledWith("/api/auth/me", { signal: controller.signal, allowDevelopmentFallback: false });
  });

  it.each([{}, { username: "" }, { username: "   " }, { username: 7 }])("returns null for a missing username %#", async (body) => {
    get.mockResolvedValueOnce(body);
    await expect(getSignedInUsername()).resolves.toBeNull();
  });

  it.each([401, 403, 404])("returns null when no one is signed in (%i)", async (status) => {
    get.mockRejectedValueOnce(new ApiError({ path: "/api/auth/me", status }));
    await expect(getSignedInUsername()).resolves.toBeNull();
  });

  it("propagates other failures", async () => {
    const failure = new ApiError({ path: "/api/auth/me", status: 500 });
    get.mockRejectedValueOnce(failure);
    await expect(getSignedInUsername()).rejects.toBe(failure);
  });
});
