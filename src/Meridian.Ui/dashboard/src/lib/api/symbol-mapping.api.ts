import { deleteJson, getJson, postFormData, postJson, putJson, type ApiRequestOptions } from "@/lib/api/http-client";
import type { CanonicalSymbolResolutionMismatchResponse, SymbolResolutionMode } from "@/types";

export interface SymbolMapping {
  canonicalSymbol: string;
  ibSymbol: string | null;
  alpacaSymbol: string | null;
  polygonSymbol: string | null;
  yahooSymbol: string | null;
  name: string | null;
  figi: string | null;
}
export interface SymbolMappingRollout {
  mismatchCount: number;
  providers: Array<{
    provider: string;
    savedMode: SymbolResolutionMode;
    activeMode: SymbolResolutionMode | null;
    pendingRestart: boolean;
    recentMismatches: CanonicalSymbolResolutionMismatchResponse[];
  }>;
}
export interface SymbolMappingRolloutCommand {
  mode: "Compare" | "Canonical";
  expectedMode: SymbolResolutionMode;
  reviewedMismatchCount: number;
  evidenceReviewed: boolean;
  confirmArchivesNotRewritten: boolean;
  confirmSeriesRemainSeparate: boolean;
  reason: string;
}

const path = "/api/symbols/mappings";
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
export const symbolMappingApi = {
  list: (options: ApiRequestOptions = {}) => getJson<SymbolMapping[]>(path, authoritative(options)),
  rollout: (options: ApiRequestOptions = {}) => getJson<SymbolMappingRollout>(`${path}/rollout`, authoritative(options)),
  save: (mapping: SymbolMapping, options: ApiRequestOptions = {}) => postJson<void>(path, mapping, options),
  remove: (symbol: string, options: ApiRequestOptions = {}) => deleteJson<void>(`${path}/${encodeURIComponent(symbol)}`, options),
  importCsv: (file: File, options: ApiRequestOptions = {}) => {
    const form = new FormData();
    form.append("file", file);
    return postFormData<{ imported: number }>(`${path}/import`, form, options);
  },
  saveRollout: (provider: string, command: SymbolMappingRolloutCommand, options: ApiRequestOptions = {}) =>
    putJson<{ pendingRestart: boolean }>(`${path}/rollout/${encodeURIComponent(provider)}`, command, options)
};
