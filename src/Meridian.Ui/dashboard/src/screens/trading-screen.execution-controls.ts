import type {
  ExecutionCircuitBreakerActivationResponse,
  ExecutionControlSnapshot,
  TradingActionResult
} from "@/types";
import type { ExecutionControlsPanel } from "@/screens/trading-screen.view-model";

export function buildExecutionControlsPanel(snapshot: ExecutionControlSnapshot): ExecutionControlsPanel {
  const breakerOpen = snapshot.circuitBreaker.isOpen;
  const symbolLimitCount = Object.keys(snapshot.symbolPositionLimits).length;
  const overrideCount = snapshot.manualOverrides.length;

  return {
    title: "Execution controls snapshot",
    breakerAction: { kind: breakerOpen ? "close-circuit-breaker" : "open-circuit-breaker" },
    breakerActionLabel: breakerOpen ? "Reset breaker" : "Open breaker",
    breakerActionDisabled: false,
    breakerActionDisabledReason: null,
    breakerActionAriaLabel: breakerOpen
      ? "Reset the execution circuit breaker and allow order submission to resume"
      : "Open the execution circuit breaker to halt submission and cancel all open orders",
    statusLabel: `Breaker ${breakerOpen ? "Open" : "Closed"}`,
    statusTone: breakerOpen ? "danger" : "success",
    ariaLabel: `Execution controls snapshot: breaker ${breakerOpen ? "open" : "closed"}, ${symbolLimitCount} symbol ${symbolLimitCount === 1 ? "limit" : "limits"}, ${overrideCount} active ${overrideCount === 1 ? "override" : "overrides"}.`,
    rows: [
      {
        id: "default-limit",
        label: "Default limit",
        value: snapshot.defaultMaxPositionSize === null ? "Not set" : String(snapshot.defaultMaxPositionSize)
      },
      {
        id: "symbol-limits",
        label: "Symbol limits",
        value: formatExecutionSymbolLimits(snapshot.symbolPositionLimits)
      },
      {
        id: "active-overrides",
        label: "Active overrides",
        value: formatExecutionManualOverrides(snapshot.manualOverrides)
      },
      {
        id: "as-of",
        label: "As of",
        value: snapshot.asOf
      }
    ]
  };
}

function formatExecutionSymbolLimits(limits: Record<string, number>): string {
  const entries = Object.entries(limits);
  if (entries.length === 0) {
    return "None";
  }

  return entries.map(([symbol, limit]) => `${symbol}=${limit}`).join(", ");
}

function formatExecutionManualOverrides(overrides: ExecutionControlSnapshot["manualOverrides"]): string {
  if (overrides.length === 0) {
    return "None";
  }

  return overrides
    .map((entry) => `${entry.kind}${entry.symbol ? ` (${entry.symbol})` : ""}`)
    .join(", ");
}

/**
 * Derives the operator verdict from the state the server returned and the sweep it ran, never from
 * the fact that the request succeeded. A 200 that does not confirm the requested breaker state, or
 * that reports a sweep which left orders working, has to read as a failure - an execution control
 * that reports success while the book is still live is the exact failure this surface exists to
 * prevent.
 */
export function buildCircuitBreakerActionResult(
  shouldOpen: boolean,
  response: ExecutionCircuitBreakerActivationResponse
): TradingActionResult {
  const actionId = `act-${Date.now()}`;
  const occurredAt = new Date().toISOString();
  const confirmedOpen = response.circuitBreaker?.isOpen;

  if (confirmedOpen !== shouldOpen) {
    return {
      actionId,
      status: "Failed",
      message: shouldOpen
        ? "The circuit breaker did NOT open: the workstation API did not confirm the halt. Verify the book at the broker."
        : "The circuit breaker did NOT reset: the workstation API did not confirm the change. Re-check execution controls before submitting orders.",
      occurredAt
    };
  }

  if (!shouldOpen) {
    return {
      actionId,
      status: "Completed",
      message: "Circuit breaker reset. Order submission can resume.",
      occurredAt
    };
  }

  const sweep = response.sweep ?? null;
  if (!sweep) {
    return {
      actionId,
      status: "Completed",
      message: "Circuit breaker opened. Order submission is halted; no cancel-all sweep was reported.",
      occurredAt
    };
  }

  const stillWorking = sweep.stillWorking ?? [];
  const sentences = [
    `Circuit breaker opened. Cancelled ${sweep.cancelled} of ${sweep.requested} open ${sweep.requested === 1 ? "order" : "orders"}.`
  ];

  if (stillWorking.length > 0) {
    sentences.push(
      `Still working - cancel by hand: ${stillWorking.map((failure) => failure.orderId).join(", ")}.`
    );
  }

  if (sweep.brokerViewUnavailable) {
    sentences.push(
      `The broker book could not be read${sweep.brokerViewError ? ` (${sweep.brokerViewError})` : ""}, so an empty local book does not prove the broker book is empty. Verify at the broker.`
    );
  }

  return {
    actionId,
    status: sweep.outcome,
    message: sentences.join(" "),
    occurredAt,
    stillWorking: stillWorking.length > 0 ? stillWorking : null,
    brokerViewUnavailable: sweep.brokerViewUnavailable ?? null
  };
}
