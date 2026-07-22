export type SymbolResolutionMode = "Legacy" | "Compare" | "Canonical";

export interface CanonicalSymbolIdentifiersResponse {
  isin: string | null;
  figi: string | null;
  compositeFigi: string | null;
  cusip: string | null;
  sedol: string | null;
}

export interface CanonicalSymbolAliasResponse {
  alias: string;
  source: string | null;
  provider: string | null;
  validFrom: string | null;
  validTo: string | null;
  isActive: boolean;
}

export interface CanonicalProviderAliasResponse {
  provider: string;
  symbol: string;
  source: string;
  isOverride: boolean;
  updatedAt: string | null;
}

export interface CanonicalSymbolRegistryEntryResponse {
  securityId: string | null;
  canonicalTicker: string;
  displayName: string | null;
  assetClass: string;
  exchange: string | null;
  currency: string | null;
  identifiers: CanonicalSymbolIdentifiersResponse;
  aliases: CanonicalSymbolAliasResponse[];
  providerAliases: CanonicalProviderAliasResponse[];
  provenanceSources: string[];
  hasRecentMismatch: boolean;
}

export interface CanonicalSymbolResolutionMismatchResponse {
  input: string;
  fromProvider: string;
  toProvider: string;
  legacyResult: string | null;
  canonicalResult: string | null;
  securityId: string | null;
  observedAt: string;
}

export interface CanonicalSymbolMigrationResponse {
  migrationId: string;
  sourceFingerprint: string;
}

export interface CanonicalSymbolRegistryResponse {
  registryVersion: string;
  resolutionMode: SymbolResolutionMode;
  compareModeReturnsLegacy: boolean;
  totalMismatchCount: number;
  lastMismatchAt: string | null;
  recentMismatches: CanonicalSymbolResolutionMismatchResponse[];
  migrations: CanonicalSymbolMigrationResponse[];
  symbols: CanonicalSymbolRegistryEntryResponse[];
}
