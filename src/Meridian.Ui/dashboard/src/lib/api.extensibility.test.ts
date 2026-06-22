import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  activateExtensibilityTenantTemplate,
  getExtensibilityCatalog,
  getExtensibilityTenantTemplateReadiness,
  listExtensibilityTenantTemplateActivations,
  listExtensibilityTenantTemplates,
  resetDevelopmentFixtureUsage,
  saveExtensibilityTenantTemplate
} from "@/lib/api";
import type { TenantTemplateConfigurationBundle } from "@/types";

describe("core extensibility API wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({}),
      text: async () => "{}"
    });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("loads and mutates tenant template configuration through shared workstation routes", async () => {
    const controller = new AbortController();
    const template: TenantTemplateConfigurationBundle = {
      tenantTemplateId: "fund admin / v1",
      displayName: "Fund Admin v1",
      profile: "FundAdministrator",
      configurations: [],
      domainExtensions: [],
      allowsCoreObjectIdentityOverrides: false,
      allowsAuditTrailOverrides: false,
      allowsCalculationOverrides: false
    };

    await getExtensibilityCatalog({ signal: controller.signal });
    await listExtensibilityTenantTemplates({ signal: controller.signal });
    await saveExtensibilityTenantTemplate(template, { signal: controller.signal });
    await activateExtensibilityTenantTemplate(
      "fund admin / v1",
      {
        changeReason: "Ops reviewed template bundle",
        linkedAuditEventId: "audit / 1"
      },
      { signal: controller.signal }
    );
    await listExtensibilityTenantTemplateActivations("fund admin / v1", { signal: controller.signal });
    await getExtensibilityTenantTemplateReadiness("fund admin / v1", { signal: controller.signal });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/workstation/extensibility/catalog",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/workstation/extensibility/tenant-templates",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1",
      expect.objectContaining({
        method: "PUT",
        signal: controller.signal,
        body: JSON.stringify(template)
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/activate",
      expect.objectContaining({
        method: "POST",
        signal: controller.signal,
        body: JSON.stringify({
          changeReason: "Ops reviewed template bundle",
          linkedAuditEventId: "audit / 1"
        })
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      5,
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/activations",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      6,
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/readiness",
      expect.objectContaining({ signal: controller.signal })
    );
  });
});
