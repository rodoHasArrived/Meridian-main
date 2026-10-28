import type { ApiRequestOptions } from "@/lib/api";
import type { ProviderRoutingConnection } from "@/types";

export interface ProviderCredentialRequestOptions extends ApiRequestOptions { connectionId?: string }

/** Legacy callers omit scope; migrated editors must pass an explicit, nonempty connection. */
export function providerConnectionScopeEndpoint(path: string, connectionId?: string): string {
  if (connectionId === undefined) return path;
  if (!connectionId.trim()) throw new Error("An owned connection must be selected.");
  return `${path}${path.includes("?") ? "&" : "?"}connectionId=${encodeURIComponent(connectionId)}`;
}

// Exact aliases from Meridian.ProviderSdk/ProviderIdentity.cs; the contract test guards parity.
// Unknown plugin IDs keep their full identity, including meaningful integration suffixes.
const providerFamilyAliases = new Map<string, string>([
  ["alpaca-api", "alpaca"],
  ["alpaca-brokerage", "alpaca"],
  ["alpaca-corp-actions", "alpaca"],
  ["alpaca-options", "alpaca"],
  ["alpaca-symbols", "alpaca"],
  ["alpha-vantage", "alphavantage"],
  ["alpha_vantage", "alphavantage"],
  ["alphavantage-corp-actions", "alphavantage"],
  ["alphavantage-symbols", "alphavantage"],
  ["edgar-symbols", "edgar"],
  ["finnhub-corp-actions", "finnhub"],
  ["finnhub-symbols", "finnhub"],
  ["fred-symbols", "fred"],
  ["ib", "ibkr"],
  ["interactivebrokers", "ibkr"],
  ["interactive-brokers", "ibkr"],
  ["interactive_brokers", "ibkr"],
  ["ibflex", "ib-flex"],
  ["ib-flex-web-service", "ib-flex"],
  ["nasdaqdatalink", "nasdaq"],
  ["nasdaq-data-link", "nasdaq"],
  ["nasdaq_data_link", "nasdaq"],
  ["nasdaq-corp-actions", "nasdaq"],
  ["nasdaq-symbols", "nasdaq"],
  ["nyse-streaming", "nyse"],
  ["open-figi", "openfigi"],
  ["openfigi-api", "openfigi"],
  ["plaid-api", "plaid"],
  ["polygon-options", "polygon"],
  ["polygonio", "polygon"],
  ["polygon-io", "polygon"],
  ["polygon-symbols", "polygon"],
  ["robinhood-brokerage", "robinhood"],
  ["robinhood-live", "robinhood"],
  ["robinhood-options", "robinhood"],
  ["robinhood-symbols", "robinhood"],
  ["ssharp", "stocksharp"],
  ["synthetic-options", "synthetic"],
  ["template-brokerage", "templates"],
  ["tiingo-corp-actions", "tiingo"],
  ["tiingo-symbols", "tiingo"],
  ["twelve-data", "twelvedata"],
  ["twelve_data", "twelvedata"],
  ["twelvedata-corp-actions", "twelvedata"],
  ["twelvedata-symbols", "twelvedata"],
  ["twelvedata-api", "twelvedata"],
  ["yahoofinance", "yahoo"],
  ["yahoo-finance", "yahoo"],
  ["qbo", "quickbooks"],
  ["quickbooks-online", "quickbooks"],
  ["qbo-fixture", "quickbooks-fixture"]
]);

function canonicalCredentialProviderFamily(value: string | null | undefined): string {
  const normalized = value?.trim().toLowerCase() ?? "";
  return providerFamilyAliases.get(normalized) ?? normalized;
}

export function ownedCredentialConnections(rows: ProviderRoutingConnection[], providerId: string): ProviderRoutingConnection[] {
  const canonical = canonicalCredentialProviderFamily(providerId);
  if (!canonical) return [];
  const counts = new Map<string, number>();
  for (const row of rows) {
    const id = row.connectionId?.toLowerCase();
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  return rows.filter(row => row.connectionId?.trim() && counts.get(row.connectionId.toLowerCase()) === 1 &&
    canonicalCredentialProviderFamily(row.providerFamilyId) === canonical && row.enabled &&
    row.tenantId?.trim() && row.externalAccountId?.trim() && row.credentialEnvironment?.trim());
}

export function credentialOwnershipKey(row: ProviderRoutingConnection): string {
  return JSON.stringify([row.providerFamilyId, row.connectionId, row.tenantId, row.externalAccountId, row.credentialEnvironment]);
}
