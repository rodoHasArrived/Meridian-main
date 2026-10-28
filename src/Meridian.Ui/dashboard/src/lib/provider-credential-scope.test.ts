import { beforeEach, describe, expect, it, vi } from "vitest";
import { deleteProviderCredentials, getProviderConnections, putProviderCredentials, verifyProviderConnection } from "./api";
import { credentialOwnershipKey, ownedCredentialConnections, providerConnectionScopeEndpoint } from "./provider-credential-scope";
import type { ProviderRoutingConnection } from "@/types";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const connection = (id: string): ProviderRoutingConnection => ({ connectionId: id, providerFamilyId: "alpaca", displayName: id, tenantId: "tenant-a", externalAccountId: "account-a", credentialEnvironment: "paper", enabled: true } as ProviderRoutingConnection);
describe("credential connection scope", () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset().mockResolvedValue({ ok: true, status: 200, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });
  it("encodes the exact owned connection on every credential operation", async () => {
    const options = { connectionId: "account / A", allowDevelopmentFallback: false };
    await getProviderConnections(options);
    await putProviderCredentials("alpaca", { credentials: { KeyId: "value" } }, options);
    await verifyProviderConnection("alpaca", options);
    await deleteProviderCredentials("alpaca", options);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "/api/providers/connections?connectionId=account%20%2F%20A",
      "/api/providers/alpaca/credentials?connectionId=account%20%2F%20A",
      "/api/providers/alpaca/verify?connectionId=account%20%2F%20A",
      "/api/providers/alpaca/credentials?connectionId=account%20%2F%20A"
    ]);
    expect(fetchMock.mock.calls.map(([, init]) => init.method ?? "GET")).toEqual(["GET", "PUT", "POST", "DELETE"]);
  });
  it("refuses explicitly empty scope instead of becoming provider-wide", () => {
    expect(() => providerConnectionScopeEndpoint("/endpoint", "  ")).toThrow(/owned connection/);
    expect(() => putProviderCredentials("alpaca", { credentials: {} }, { connectionId: "" })).toThrow(/owned connection/);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(providerConnectionScopeEndpoint("/endpoint")).toBe("/endpoint");
  });
  it("excludes ambiguous and incomplete targets and binds all retained dimensions", () => {
    const one = connection("one");
    const duplicates = [connection("duplicate"), connection("DUPLICATE")];
    expect(ownedCredentialConnections([one, ...duplicates, { ...one, connectionId: "missing", credentialEnvironment: null }, { ...one, connectionId: "other", providerFamilyId: "polygon" }], "alpaca")).toEqual([one]);
    expect(credentialOwnershipKey(one)).not.toBe(credentialOwnershipKey({ ...one, tenantId: "other" }));
    expect(credentialOwnershipKey(one)).not.toBe(credentialOwnershipKey({ ...one, externalAccountId: "other" }));
    expect(credentialOwnershipKey(one)).not.toBe(credentialOwnershipKey({ ...one, credentialEnvironment: "live" }));
  });

  it.each([
    ["alpha-vantage", "alphavantage"], [" ALPHA_VANTAGE ", "alphavantage"],
    ["qbo", "quickbooks"], ["quickbooks-online", "quickbooks"],
    ["polygonio", "polygon"], ["alpaca-brokerage", "alpaca"],
    ["nasdaq-data-link", "nasdaq"], ["ibflex", "ib-flex"]
  ])("selects retained family alias %s for %s without rewriting ownership", (alias, canonical) => {
    const retained = { ...connection("Retained / Account A"), providerFamilyId: alias };
    const originalKey = credentialOwnershipKey(retained);
    const selected = ownedCredentialConnections([retained], canonical);
    expect(selected).toEqual([retained]);
    expect(selected[0]).toBe(retained);
    expect(credentialOwnershipKey(selected[0])).toBe(originalKey);
    expect(providerConnectionScopeEndpoint("/credentials", selected[0].connectionId))
      .toBe("/credentials?connectionId=Retained%20%2F%20Account%20A");
  });

  it("matches the authoritative shared provider alias catalog", () => {
    const source = readFileSync(resolve(process.cwd(), "../../Meridian.ProviderSdk/ProviderIdentity.cs"), "utf8");
    const aliases = Array.from(source.matchAll(/\["([^"]+)"\]\s*=\s*"([^"]+)"/g), match => [match[1], match[2]]);
    expect(aliases.length).toBeGreaterThan(30);
    for (const [alias, canonical] of aliases) {
      const retained = { ...connection(alias), providerFamilyId: alias };
      expect(ownedCredentialConnections([retained], canonical), `${alias} must resolve to ${canonical}`).toEqual([retained]);
    }
  });

  it("keeps different integrations, unknown plugin suffixes and ownership identities distinct", () => {
    const flex = { ...connection("flex"), providerFamilyId: "ibflex" };
    const simulation = { ...connection("sim"), providerFamilyId: "ib-sim" };
    const plugin = { ...connection("plugin"), providerFamilyId: "acme-options" };
    expect(ownedCredentialConnections([flex, simulation], "ibkr")).toEqual([]);
    expect(ownedCredentialConnections([plugin], "acme")).toEqual([]);
    expect(ownedCredentialConnections([plugin], "ACME-OPTIONS")).toEqual([plugin]);
    const alias = { ...connection("owned"), providerFamilyId: "qbo" };
    for (const changed of [
      { ...alias, tenantId: "other-tenant" }, { ...alias, externalAccountId: "other-account" },
      { ...alias, credentialEnvironment: "production" }, { ...alias, connectionId: "other-id" },
      { ...alias, providerFamilyId: "quickbooks" }
    ]) expect(credentialOwnershipKey(changed)).not.toBe(credentialOwnershipKey(alias));
  });

  it("still refuses ambiguous, disabled and incomplete ownership when family aliases match", () => {
    const retained = { ...connection("owned"), providerFamilyId: "qbo" };
    for (const invalid of [
      { ...retained, enabled: false }, { ...retained, tenantId: null },
      { ...retained, externalAccountId: "" }, { ...retained, credentialEnvironment: null }
    ]) expect(ownedCredentialConnections([invalid], "quickbooks")).toEqual([]);
    expect(ownedCredentialConnections([retained, { ...retained, connectionId: "OWNED", providerFamilyId: "quickbooks" }], "quickbooks")).toEqual([]);
  });
});
