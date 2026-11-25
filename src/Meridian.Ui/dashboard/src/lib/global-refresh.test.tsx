import { render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { GlobalRefreshProvider, useOnGlobalRefresh } from "@/lib/global-refresh";

function Panel({ reload }: { reload: () => void }) {
  useOnGlobalRefresh(reload);
  return null;
}

describe("global refresh", () => {
  it("reloads page-owned data when the header Refresh is pressed, and not on mount", () => {
    const reload = vi.fn();
    const { rerender } = render(<GlobalRefreshProvider sequence={0}><Panel reload={reload} /></GlobalRefreshProvider>);
    expect(reload).not.toHaveBeenCalled();

    rerender(<GlobalRefreshProvider sequence={1}><Panel reload={reload} /></GlobalRefreshProvider>);
    expect(reload).toHaveBeenCalledTimes(1);

    rerender(<GlobalRefreshProvider sequence={1}><Panel reload={reload} /></GlobalRefreshProvider>);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("does nothing outside a provider", () => {
    const reload = vi.fn();
    render(<Panel reload={reload} />);
    expect(reload).not.toHaveBeenCalled();
  });
});
