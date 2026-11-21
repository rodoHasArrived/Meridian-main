import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { buildShareableLink, CopyLinkButton } from "@/components/meridian/copy-link-button";
import { ToastProvider } from "@/components/ui/toast";

describe("CopyLinkButton", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("copies the current URL and confirms with a toast", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText } });

    render(
      <ToastProvider>
        <CopyLinkButton />
      </ToastProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Copy link to this view" }));

    expect(await screen.findByText("Link copied")).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith(window.location.href);
  });

  it("falls back to a danger toast when the clipboard is unavailable", async () => {
    vi.stubGlobal("navigator", { ...navigator, clipboard: undefined });

    render(
      <ToastProvider>
        <CopyLinkButton />
      </ToastProvider>
    );

    fireEvent.click(screen.getByRole("button", { name: "Copy link to this view" }));

    expect(await screen.findByText("Copy failed")).toBeInTheDocument();
  });

  it("carries every operating-scope dimension the route did not write into its URL (C1)", () => {
    const link = buildShareableLink("https://host/workstation/settings?view=abc", {
      symbol: "AAPL",
      fundAccountId: "fund-a",
      from: "2026-06-01"
    });
    const url = new URL(link);
    expect(url.pathname).toBe("/workstation/settings");
    expect(url.searchParams.get("view")).toBe("abc");
    expect(url.searchParams.get("symbol")).toBe("AAPL");
    expect(url.searchParams.get("fundAccountId")).toBe("fund-a");
    expect(url.searchParams.get("from")).toBe("2026-06-01");
  });

  it("never overrides a scope value the URL already carries", () => {
    const link = buildShareableLink("https://host/workstation/trading?symbol=MSFT", { symbol: "AAPL" });
    expect(new URL(link).searchParams.getAll("symbol")).toEqual(["MSFT"]);
  });
});
