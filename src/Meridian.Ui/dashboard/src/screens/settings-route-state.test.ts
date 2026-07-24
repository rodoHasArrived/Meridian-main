import { describe, expect, it } from "vitest";
import {
  canonicalSettingsRouteForState,
  resolveSettingsRouteState
} from "@/screens/settings-route-state";

describe("settings route state", () => {
  it("resolves the canonical task routes", () => {
    expect(resolveSettingsRouteState("/settings")).toMatchObject({ view: "chooser", providerId: null, legacyAlias: false });
    expect(resolveSettingsRouteState("/settings/preferences")).toMatchObject({ view: "preferences", legacyAlias: false });
    expect(resolveSettingsRouteState("/settings/access")).toMatchObject({ view: "access", legacyAlias: false });
    expect(resolveSettingsRouteState("/settings/accounting-systems")).toMatchObject({ view: "operations", legacyAlias: false });
    expect(resolveSettingsRouteState("/settings/providers/alpaca/setup")).toMatchObject({ view: "provider-setup", providerId: "alpaca" });
    expect(resolveSettingsRouteState("/settings/providers/polygon/advanced")).toMatchObject({ view: "provider-advanced", providerId: "polygon" });
    expect(resolveSettingsRouteState("/settings/diagnostics/advanced")).toMatchObject({ view: "diagnostics-advanced", legacyAlias: false });
  });

  it("keeps legacy paths and hashes as compatibility aliases", () => {
    expect(resolveSettingsRouteState("/settings/integrations")).toMatchObject({ view: "operations", legacyAlias: true });
    expect(resolveSettingsRouteState("/settings/feature-coverage")).toMatchObject({ view: "diagnostics-advanced", legacyAlias: true });
    expect(resolveSettingsRouteState("/settings", "#alpaca-provider-setup")).toMatchObject({
      view: "provider-setup",
      providerId: "alpaca",
      legacyAlias: true
    });
    expect(resolveSettingsRouteState("/settings", "#provider-quickbooks-connection")).toMatchObject({
      view: "provider-setup",
      providerId: "quickbooks",
      legacyAlias: true
    });
    expect(resolveSettingsRouteState("/settings", "#backend-capability-coverage")).toMatchObject({
      view: "diagnostics-advanced",
      legacyAlias: true
    });
  });

  it("lets canonical object routes win over stale hashes", () => {
    expect(resolveSettingsRouteState("/settings/preferences", "#provider-connection-center")?.view).toBe("preferences");
    expect(resolveSettingsRouteState("/settings/providers", "#settings-overview")?.view).toBe("providers");
    expect(resolveSettingsRouteState("/settings/diagnostics", "#runtime-feature-capabilities")?.view).toBe("diagnostics");
  });

  it("builds canonical destinations for focused provider and diagnostics states", () => {
    expect(canonicalSettingsRouteForState({ view: "provider-setup", providerId: "QuickBooks" })).toBe(
      "/settings/providers/quickbooks/setup"
    );
    expect(canonicalSettingsRouteForState({ view: "provider-advanced", providerId: "Polygon" })).toBe(
      "/settings/providers/polygon/advanced"
    );
    expect(canonicalSettingsRouteForState({ view: "diagnostics-advanced", providerId: null })).toBe(
      "/settings/diagnostics/advanced"
    );
  });
});
