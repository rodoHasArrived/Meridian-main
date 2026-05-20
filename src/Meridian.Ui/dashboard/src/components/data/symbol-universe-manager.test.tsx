import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SymbolUniverseManager } from "@/components/data/symbol-universe-manager";

describe("SymbolUniverseManager", () => {
  it("names the add-symbol dialog from its title and description", async () => {
    const user = userEvent.setup();
    render(
      <SymbolUniverseManager
        symbols={[]}
        onAdd={vi.fn().mockResolvedValue(undefined)}
        onUpdate={vi.fn().mockResolvedValue(undefined)}
        onDelete={vi.fn().mockResolvedValue(undefined)}
      />
    );

    await user.click(screen.getByRole("button", { name: /add symbol/i }));

    const dialog = screen.getByRole("dialog", { name: /add symbol/i });
    expect(dialog).toHaveAccessibleDescription("Configure a symbol for market data collection and trading.");
  });
});
