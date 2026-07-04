import type { ComponentProps } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { ScopePicker } from "@/components/meridian/scope-picker";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { SymbolRecord } from "@/types";

const FUND_ACCOUNTS: ScopeFundAccountOption[] = [
  { id: "fund-a", label: "Alpha Fund" },
  { id: "fund-b", label: "Beta Fund" }
];

function symbolRecord(symbol: string, provider: string): SymbolRecord {
  return { symbol, provider } as unknown as SymbolRecord;
}

function renderPicker(overrides: Partial<ComponentProps<typeof ScopePicker>> = {}) {
  const onApply = vi.fn();
  const onOpenChange = vi.fn();
  const scope: AppShellOperatingScopeInput = overrides.scope ?? {};
  const searchSymbols = overrides.searchSymbols ?? vi.fn().mockResolvedValue([]);
  render(
    <ScopePicker
      open
      onOpenChange={onOpenChange}
      scope={scope}
      fundAccounts={FUND_ACCOUNTS}
      onApply={onApply}
      searchSymbols={searchSymbols}
      {...overrides}
    />
  );
  return { onApply, onOpenChange, searchSymbols };
}

describe("ScopePicker", () => {
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("seeds the fields from the active scope when opened", () => {
    renderPicker({ scope: { symbol: "AAPL", fundAccountId: "fund-a", provider: "alpaca" } });

    expect(screen.getByLabelText("Subject symbol")).toHaveValue("AAPL");
    expect(screen.getByLabelText("Fund account")).toHaveValue("fund-a");
    expect(screen.getByLabelText("Provider")).toHaveValue("alpaca");
  });

  it("shows live symbol suggestions and applies the clicked one", async () => {
    const searchSymbols = vi.fn().mockResolvedValue([symbolRecord("MSFT", "alpaca"), symbolRecord("MSTR", "alpaca")]);
    const { onApply } = renderPicker({ searchSymbols });

    fireEvent.change(screen.getByLabelText("Subject symbol"), { target: { value: "ms" } });

    const suggestion = await screen.findByRole("button", { name: /MSFT/ });
    fireEvent.click(suggestion);

    expect(searchSymbols).toHaveBeenCalledWith("ms", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByLabelText("Subject symbol")).toHaveValue("MSFT");

    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ symbol: "MSFT" }));
  });

  it("emits the complete next scope on apply, carrying untouched dimensions", () => {
    const { onApply, onOpenChange } = renderPicker({
      scope: { symbol: "AAPL", fundAccountId: "fund-a", provider: "alpaca", runId: "run-9", from: "2026-01-01" }
    });

    fireEvent.change(screen.getByLabelText("Provider"), { target: { value: "polygon" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));

    expect(onApply).toHaveBeenCalledWith({
      symbol: "AAPL",
      fundAccountId: "fund-a",
      provider: "polygon",
      runId: "run-9",
      from: "2026-01-01",
      to: null,
      date: null,
      asOf: null
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("clears the entire scope from the clear action", () => {
    const { onApply } = renderPicker({ scope: { symbol: "AAPL", provider: "alpaca" } });

    fireEvent.click(screen.getByRole("button", { name: "Clear scope" }));

    expect(onApply).toHaveBeenCalledWith({});
  });
});
