import type { ProviderKindMeta } from "@/types";
import type { ProviderSetupFormState } from "./data-screen.provider-setup.types";

export const PROVIDER_KIND_CATALOG: ProviderKindMeta[] = [
  {
    kind: "polygon",
    label: "Polygon.io",
    description: "Real-time and historical US equities, options, forex, and crypto.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["streaming", "backfill", "reference"]
  },
  {
    kind: "databento",
    label: "Databento",
    description: "High-quality historical bars and tick data across asset classes.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["backfill", "reference"]
  },
  {
    kind: "alpaca",
    label: "Alpaca",
    description: "Commission-free US equities broker with market data and paper trading.",
    needsApiKey: true,
    needsApiSecret: true,
    needsEndpoint: false,
    defaultCapabilities: ["streaming", "backfill", "brokerage"]
  },
  {
    kind: "interactivebrokers",
    label: "Interactive Brokers",
    description: "Full-service brokerage with live order routing and market data.",
    needsApiKey: false,
    needsApiSecret: false,
    needsEndpoint: true,
    defaultCapabilities: ["streaming", "brokerage"]
  },
  {
    kind: "plaid",
    label: "Plaid",
    description: "Bank, balance, transaction, identity, and investment account evidence for treasury and reconciliation workflows.",
    needsApiKey: true,
    needsApiSecret: true,
    needsEndpoint: false,
    defaultCapabilities: ["banking", "identity", "investments"],
    apiKeyLabel: "Client ID",
    apiKeyAriaLabel: "Plaid client ID",
    apiSecretLabel: "Secret",
    apiSecretAriaLabel: "Plaid secret"
  },
  {
    kind: "yahoo",
    label: "Yahoo Finance",
    description: "Free end-of-day historical prices. No API key required.",
    needsApiKey: false,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["backfill"]
  },
  {
    kind: "custom",
    label: "Custom data connection",
    description: "Connect a custom or internal data provider using its service URL.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: true,
    defaultCapabilities: []
  }
];

export const ALL_CAPABILITIES: Array<{ id: string; label: string; description: string }> = [
  { id: "streaming", label: "Live streaming", description: "Real-time price ticks and quote updates" },
  { id: "backfill", label: "Historical backfill", description: "OHLCV bars and tick history" },
  { id: "reference", label: "Reference data", description: "Security master, identifiers, corporate actions" },
  { id: "brokerage", label: "Brokerage / order routing", description: "Order submission and fill feed" },
  { id: "banking", label: "Bank account evidence", description: "Balances, transactions, and bank-link evidence" },
  { id: "identity", label: "Identity verification", description: "Account-owner identity and verification evidence" },
  { id: "investments", label: "Investment holdings", description: "Investment account holdings and activity" },
  { id: "transfers", label: "Sandbox transfers", description: "Sandbox transfer authorization and payment evidence" }
];

export const defaultProviderSetupForm: ProviderSetupFormState = {
  kind: "yahoo",
  displayName: "Yahoo Finance",
  environment: "paper",
  liveAcknowledged: false,
  apiKey: "",
  apiSecret: "",
  endpoint: "",
  capabilities: ["backfill"]
};

export function resolveProviderKindMeta(kind: ProviderSetupFormState["kind"]): ProviderKindMeta | undefined {
  return PROVIDER_KIND_CATALOG.find((provider) => provider.kind === kind);
}

export function formatProviderCapabilityLabel(capabilityId: string): string {
  return ALL_CAPABILITIES.find((capability) => capability.id === capabilityId)?.label ?? capabilityId;
}

export function resolveProviderSetupNextStep(capabilities: string[]): string {
  if (capabilities.includes("banking") || capabilities.includes("identity") || capabilities.includes("investments")) {
    return "Link account evidence";
  }

  if (capabilities.includes("transfers")) {
    return "Review sandbox transfers";
  }

  if (capabilities.includes("backfill")) {
    return "Preview a historical backfill";
  }

  if (capabilities.includes("streaming")) {
    return "Validate live quotes";
  }

  if (capabilities.includes("brokerage")) {
    return "Check Trading readiness";
  }

  if (capabilities.includes("reference")) {
    return "Review Security Master";
  }

  return "Select a capability";
}
