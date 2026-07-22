import type { ApiRequestOptions } from "@/lib/api";
import type {
  ProviderModuleCatalogueEntry,
  ProviderModuleSetupResult,
  ProviderModuleStatus,
  ProviderModuleTestResult,
  UpsertProviderModuleRequest
} from "@/types/provider-setup";

type GetJson = <T>(path: string, options?: ApiRequestOptions) => Promise<T>;
type PostJson = <T>(path: string, body?: unknown, options?: ApiRequestOptions) => Promise<T>;
type PutJson = <T>(path: string, body?: unknown, options?: ApiRequestOptions) => Promise<T>;
type DeleteJson = <T>(path: string, options?: ApiRequestOptions, body?: unknown) => Promise<T>;

const PROVIDER_MODULE_API = {
  modules: "/api/providers/modules",
  catalogue: "/api/providers/modules/catalogue",
  moduleById: (moduleId: string) => `/api/providers/modules/${encodeURIComponent(moduleId)}`,
  enabled: (moduleId: string) => `/api/providers/modules/${encodeURIComponent(moduleId)}/enabled`,
  test: (moduleId: string) => `/api/providers/modules/${encodeURIComponent(moduleId)}/test`,
  restart: "/api/providers/restart"
};

export function createProviderModulesApi(
  getJson: GetJson,
  postJson: PostJson,
  putJson: PutJson,
  deleteJson: DeleteJson
) {
  return {
    getProviderModules(options: ApiRequestOptions = {}) {
      return getJson<ProviderModuleStatus[]>(PROVIDER_MODULE_API.modules, options);
    },
    getProviderModuleCatalogue(options: ApiRequestOptions = {}) {
      return getJson<ProviderModuleCatalogueEntry[]>(PROVIDER_MODULE_API.catalogue, options);
    },
    upsertProviderModule(request: UpsertProviderModuleRequest, options: ApiRequestOptions = {}) {
      return postJson<ProviderModuleSetupResult>(PROVIDER_MODULE_API.modules, request, options);
    },
    updateProviderModule(moduleId: string, request: UpsertProviderModuleRequest, options: ApiRequestOptions = {}) {
      return putJson<ProviderModuleSetupResult>(PROVIDER_MODULE_API.moduleById(moduleId), request, options);
    },
    deleteProviderModule(moduleId: string, options: ApiRequestOptions = {}) {
      return deleteJson<ProviderModuleSetupResult>(PROVIDER_MODULE_API.moduleById(moduleId), options);
    },
    setProviderModuleEnabled(moduleId: string, enabled: boolean, options: ApiRequestOptions = {}) {
      return putJson<ProviderModuleSetupResult>(PROVIDER_MODULE_API.enabled(moduleId), { enabled }, options);
    },
    testProviderModule(moduleId: string, options: ApiRequestOptions = {}) {
      return postJson<ProviderModuleTestResult>(PROVIDER_MODULE_API.test(moduleId), undefined, options);
    },
    restartProviderHost(options: ApiRequestOptions = {}) {
      return postJson<{ restarting: boolean; message: string }>(PROVIDER_MODULE_API.restart, undefined, options);
    }
  };
}
