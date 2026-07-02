import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { CopyLinkButton } from "@/components/meridian/copy-link-button";
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
});
