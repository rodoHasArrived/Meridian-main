import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { AssetSwitcher, type AssetSwitchTarget } from "./asset-switcher";
import { searchSecurities } from "@/lib/api";
import type { SecurityMasterEntry } from "@/types";

vi.mock("@/lib/api", () => ({ searchSecurities: vi.fn() }));

const recentKey = "meridian.asset-detail.recent.v1";
const current = {
  securityId: "sec-aapl",
  symbol: "AAPL",
  displayName: "Apple Inc."
};
const microsoft = {
  securityId: "sec-msft",
  symbol: "MSFT",
  displayName: "Microsoft Corp."
};

function entry(target: AssetSwitchTarget): SecurityMasterEntry {
  return {
    securityId: target.securityId,
    displayName: target.displayName,
    status: "Active",
    classification: {
      assetClass: "Equity",
      primaryIdentifierValue: target.symbol,
      primaryIdentifierKind: "Ticker",
      subType: null
    },
    economicDefinition: {
      currency: "USD",
      version: 1,
      effectiveFrom: null,
      effectiveTo: null,
      subType: null,
      assetFamily: null,
      issuerType: null
    }
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

describe("AssetSwitcher", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    vi.mocked(searchSecurities).mockResolvedValue([]);
  });

  it("searches with abort support and opens the keyboard-selected asset", async () => {
    vi.mocked(searchSecurities).mockResolvedValue([entry(current), entry(microsoft)]);
    const onSelect = vi.fn();
    const user = userEvent.setup();
    render(<AssetSwitcher {...current} onSelect={onSelect} />);

    const input = screen.getByRole("combobox", { name: "Switch asset" });
    await user.type(input, "ms");
    expect(screen.getByRole("status")).toHaveTextContent("Searching assets");
    expect(await screen.findByRole("option", { name: /Microsoft/ })).toBeInTheDocument();
    expect(searchSecurities).toHaveBeenCalledWith(
      "ms",
      12,
      false,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(screen.queryByRole("option", { name: /Apple/ })).not.toBeInTheDocument();

    await user.keyboard("{ArrowDown}{Enter}");
    expect(onSelect).toHaveBeenCalledWith(microsoft);
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(input).toHaveFocus();
  });

  it("ignores an old response when the query changes", async () => {
    const first = deferred<SecurityMasterEntry[]>();
    vi.mocked(searchSecurities).mockReturnValueOnce(first.promise).mockResolvedValueOnce([]);
    render(<AssetSwitcher {...current} onSelect={vi.fn()} />);
    const input = screen.getByRole("combobox", { name: "Switch asset" });
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "ms" } });
    await waitFor(() => expect(searchSecurities).toHaveBeenCalledTimes(1));
    const firstSignal = vi.mocked(searchSecurities).mock.calls[0][3]?.signal;
    fireEvent.change(input, { target: { value: "zz" } });
    expect(firstSignal?.aborted).toBe(true);
    await screen.findByText("No other assets match “zz”.");
    await act(async () => {
      first.resolve([entry(microsoft)]);
      await first.promise;
    });
    expect(screen.queryByRole("option", { name: /Microsoft/ })).not.toBeInTheDocument();
    expect(screen.getByText("No other assets match “zz”.")).toBeInTheDocument();
  });

  it("distinguishes failed search from no matches and supports retry", async () => {
    vi.mocked(searchSecurities)
      .mockRejectedValueOnce(new Error("Offline"))
      .mockResolvedValueOnce([entry(microsoft)]);
    const onSelect = vi.fn();
    const user = userEvent.setup();
    render(<AssetSwitcher {...current} onSelect={onSelect} />);
    await user.type(screen.getByRole("combobox"), "ms");
    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn’t search assets.");
    expect(screen.queryByText(/No other assets match/)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Retry search" }));
    await user.click(await screen.findByRole("option", { name: /Microsoft/ }));
    expect(onSelect).toHaveBeenCalledWith(microsoft);
  });

  it("keeps validated bounded browser history and allows clearing it", async () => {
    const history = Array.from({ length: 10 }, (_, index) => ({
      securityId: `sec-${index}`,
      symbol: `S${index}`,
      displayName: `Security ${index}`
    }));
    localStorage.setItem(
      recentKey,
      JSON.stringify({
        version: 1,
        items: [microsoft, microsoft, {}, ...history]
      })
    );
    const onSelect = vi.fn();
    const user = userEvent.setup();
    const { container } = render(<AssetSwitcher {...current} onSelect={onSelect} />);
    await user.click(screen.getByRole("combobox"));
    expect(screen.getByText("Recent assets on this browser")).toBeInTheDocument();
    expect(screen.getAllByRole("option")).toHaveLength(5);
    expect(JSON.parse(localStorage.getItem(recentKey) ?? "{}").items).toHaveLength(6);
    expect((await axe(container)).violations).toHaveLength(0);
    await user.click(screen.getByRole("option", { name: /Microsoft/ }));
    expect(onSelect).toHaveBeenCalledWith(microsoft);
    await user.click(screen.getByRole("combobox"));
    await user.click(screen.getByRole("button", { name: "Clear history" }));
    expect(screen.queryByRole("option")).not.toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem(recentKey) ?? "{}").items).toEqual([]);
  });

  it("tolerates invalid storage, dismisses with Escape, and clears search on asset change", async () => {
    localStorage.setItem(recentKey, "broken-json");
    const user = userEvent.setup();
    const { rerender } = render(<AssetSwitcher {...current} onSelect={vi.fn()} />);
    const input = screen.getByRole("combobox");
    await user.type(input, "ms");
    await user.keyboard("{Escape}");
    expect(input).toHaveAttribute("aria-expanded", "false");
    rerender(<AssetSwitcher {...microsoft} onSelect={vi.fn()} />);
    expect(input).toHaveValue("");
    await user.click(input);
    expect(screen.getByRole("option", { name: /Apple/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Microsoft/ })).not.toBeInTheDocument();
  });
});
