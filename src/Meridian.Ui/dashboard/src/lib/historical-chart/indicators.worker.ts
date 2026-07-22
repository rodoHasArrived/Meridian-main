/// <reference lib="webworker" />
// Off-main-thread technical-indicator computation for the candlestick chart.
// Imports only the React-free indicators module, so the worker bundle stays lean.
import { computeCandlestickIndicators, type CandlestickIndicatorSet } from "./indicators";

interface IndicatorRequest {
  id: number;
  input: number[];
}

interface IndicatorResponse {
  id: number;
  ok: boolean;
  result?: CandlestickIndicatorSet;
  error?: string;
}

// The project tsconfig ships the DOM lib, so `self` is typed as Window here;
// cast through unknown to the worker scope for the message API.
const workerScope = self as unknown as DedicatedWorkerGlobalScope;

workerScope.onmessage = (event: MessageEvent<IndicatorRequest>) => {
  const { id, input } = event.data;
  try {
    const result = computeCandlestickIndicators(input);
    const response: IndicatorResponse = { id, ok: true, result };
    workerScope.postMessage(response);
  } catch (error) {
    const response: IndicatorResponse = {
      id,
      ok: false,
      error: error instanceof Error ? error.message : "Indicator computation failed."
    };
    workerScope.postMessage(response);
  }
};
