import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { GlobalRefreshProvider } from "@/lib/global-refresh";
import { PanelReload } from "./panel-reload";

describe("PanelReload", () => {
  it("renders nothing while healthy and reloads with the header Refresh", () => {
    const reload = vi.fn();
    const { container, rerender } = render(<GlobalRefreshProvider sequence={0}><PanelReload reload={reload} failed={false} what="FX rates" /></GlobalRefreshProvider>);
    expect(container).toBeEmptyDOMElement();

    rerender(<GlobalRefreshProvider sequence={1}><PanelReload reload={reload} failed={false} what="FX rates" /></GlobalRefreshProvider>);
    expect(reload).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("shows one Retry button only after a failed load", async () => {
    const reload = vi.fn();
    render(<PanelReload reload={reload} failed what="FX rates" />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Retry loading FX rates" }));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("disables Retry while a retry is running", () => {
    render(<PanelReload reload={vi.fn()} failed busy what="FX rates" />);
    expect(screen.getByRole("button", { name: "Retry loading FX rates" })).toBeDisabled();
  });
});
