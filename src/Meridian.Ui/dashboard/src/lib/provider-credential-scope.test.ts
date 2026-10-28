import { beforeEach, describe, expect, it, vi } from "vitest";
import { deleteProviderCredentials, getProviderConnections, putProviderCredentials, verifyProviderConnection } from "./api";
import { credentialOwnershipKey, ownedCredentialConnections, providerConnectionScopeEndpoint } from "./provider-credential-scope";
import type { ProviderRoutingConnection } from "@/types";

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
});
