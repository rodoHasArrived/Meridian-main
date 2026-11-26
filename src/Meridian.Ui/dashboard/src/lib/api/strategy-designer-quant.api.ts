// Strategy Designer and Quant Lab endpoints. Re-exported through @/lib/api.
import { mapQuantRunResponseToCellResult, quantContextToParameters, quantDataIntervalMinutes } from "../quant-api-mappers";
import type { CellExecuteRequest, CellExecuteResult, DataFetchRequest, DataFetchResult, StrategyDesignDocument, StrategyDesignDraftSaveRequest, StrategyDesignDraftSaveResponse, StrategyDesignDraftSummary, StrategyDesignFieldCatalogItem, StrategyDesignPreviewResult, StrategyDesignRunBacktestRequest, StrategyDesignRunBacktestResponse, StrategyDesignTemplate, StrategyDesignValidationResult } from "@/types";
import { QUANT_API_ENDPOINTS, STRATEGY_DESIGNER_API_ENDPOINTS, strategyDesignerDraftEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { getHistoricalBars } from "@/lib/api/market-data.api";

// --- Strategy Designer ---

export function getStrategyDesignerTemplates(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignTemplate[]>(STRATEGY_DESIGNER_API_ENDPOINTS.templates, options);
}

export function getStrategyDesignerFieldCatalog(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignFieldCatalogItem[]>(STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog, options);
}

export function getStrategyDesignerDrafts(options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignDraftSummary[]>(STRATEGY_DESIGNER_API_ENDPOINTS.drafts, options);
}

export function getStrategyDesignerDraft(documentId: string, options: ApiRequestOptions = {}) {
  return getJson<StrategyDesignDocument>(strategyDesignerDraftEndpoint(documentId), options);
}

export function saveStrategyDesignerDraft(request: StrategyDesignDraftSaveRequest, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignDraftSaveResponse>(STRATEGY_DESIGNER_API_ENDPOINTS.drafts, request, options);
}

export function validateStrategyDesignerDocument(document: StrategyDesignDocument, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignValidationResult>(STRATEGY_DESIGNER_API_ENDPOINTS.validate, document, options);
}

export function previewStrategyDesignerDocument(document: StrategyDesignDocument, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignPreviewResult>(STRATEGY_DESIGNER_API_ENDPOINTS.preview, document, options);
}

export function runStrategyDesignerBacktest(request: StrategyDesignRunBacktestRequest, options: ApiRequestOptions = {}) {
  return postJson<StrategyDesignRunBacktestResponse>(STRATEGY_DESIGNER_API_ENDPOINTS.runBacktest, request, options);
}

// --- Quant Lab ---

export function getQuantTemplates() {
  return getJson<import("@/types").QuantTemplatesResponse>(QUANT_API_ENDPOINTS.templates);
}

export function extractQuantParameters(source: string) {
  return postJson<import("@/types").QuantParametersResponse>(QUANT_API_ENDPOINTS.parameters, { source });
}

export function runQuantScript(request: import("@/types").QuantRunRequest) {
  return postJson<import("@/types").QuantRunResponse>(QUANT_API_ENDPOINTS.run, request);
}

export async function executeCell(request: CellExecuteRequest): Promise<CellExecuteResult> {
  const response = await runQuantScript({
    source: request.source,
    parameters: quantContextToParameters(request.context)
  });

  return mapQuantRunResponseToCellResult(request.cellId, response);
}

export async function fetchQuantData(request: DataFetchRequest, options: ApiRequestOptions = {}): Promise<DataFetchResult> {
  const intervalMinutes = quantDataIntervalMinutes(request.interval);
  const response = await getHistoricalBars(request.symbol, {
    intervalMinutes,
    from: request.from,
    to: request.to
  }, options);
  if (!response.success || response.isPartial || response.fileFailures?.length) {
    throw new Error(response.message ?? "Historical query incomplete. Repair the stored data and retry.");
  }
  const bars = response.bars.map((bar) => ({
    timestamp: bar.start,
    open: bar.open,
    high: bar.high,
    low: bar.low,
    close: bar.close,
    volume: bar.volume
  }));

  return {
    symbol: response.symbol || request.symbol.trim().toUpperCase(),
    from: response.from ?? request.from,
    to: response.to ?? request.to,
    interval: request.interval,
    bars,
    rowCount: response.totalBars || bars.length
  };
}
