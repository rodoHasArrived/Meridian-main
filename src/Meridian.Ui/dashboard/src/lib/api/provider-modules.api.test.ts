import { createProviderModulesApi } from "@/lib/api/provider-modules.api";
import type { UpsertProviderModuleRequest } from "@/types/provider-setup";

describe("provider modules api", () => {
  it("reads the module catalog with the caller's abort signal", async () => {
    const getJson = vi.fn().mockResolvedValue([]);
    const api = createProviderModulesApi(getJson, vi.fn(), vi.fn(), vi.fn());
    const controller = new AbortController();

    await api.getProviderModules({ signal: controller.signal });
    await api.getProviderModuleCatalogue({ signal: controller.signal });

    expect(getJson).toHaveBeenNthCalledWith(1, "/api/providers/modules", { signal: controller.signal });
    expect(getJson).toHaveBeenNthCalledWith(2, "/api/providers/modules/catalogue", { signal: controller.signal });
  });

  it("preserves provider-module mutation routes, bodies, and options", async () => {
    const postJson = vi.fn().mockResolvedValue({});
    const putJson = vi.fn().mockResolvedValue({});
    const deleteJson = vi.fn().mockResolvedValue({});
    const api = createProviderModulesApi(vi.fn(), postJson, putJson, deleteJson);
    const request = {} as UpsertProviderModuleRequest;
    const controller = new AbortController();
    const options = { signal: controller.signal };

    await api.upsertProviderModule(request, options);
    await api.updateProviderModule("custom/provider", request, options);
    await api.deleteProviderModule("custom/provider", options);
    await api.setProviderModuleEnabled("custom/provider", true, options);
    await api.testProviderModule("custom/provider", options);
    await api.restartProviderHost(options);

    expect(postJson).toHaveBeenNthCalledWith(1, "/api/providers/modules", request, options);
    expect(postJson).toHaveBeenNthCalledWith(2, "/api/providers/modules/custom%2Fprovider/test", undefined, options);
    expect(postJson).toHaveBeenNthCalledWith(3, "/api/providers/restart", undefined, options);
    expect(putJson).toHaveBeenNthCalledWith(1, "/api/providers/modules/custom%2Fprovider", request, options);
    expect(putJson).toHaveBeenNthCalledWith(2, "/api/providers/modules/custom%2Fprovider/enabled", { enabled: true }, options);
    expect(deleteJson).toHaveBeenCalledWith("/api/providers/modules/custom%2Fprovider", options);
  });
});
