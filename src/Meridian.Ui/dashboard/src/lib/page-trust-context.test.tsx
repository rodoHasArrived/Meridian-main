import { act, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  PageTrustProvider,
  selectWorstPageFreshness,
  usePageTrustRegistry,
  useReportPageFreshness,
  type PageTrustTone
} from "@/lib/page-trust-context";

function Strip({ tone, value }: { tone: PageTrustTone; value: string | null }) {
  useReportPageFreshness(tone, value);
  return null;
}

function Shell({ strips }: { strips: Array<{ key: string; tone: PageTrustTone; value: string | null }> }) {
  const { registry, pageFreshness } = usePageTrustRegistry();
  return (
    <PageTrustProvider registry={registry}>
      <output data-testid="worst">{pageFreshness ? `${pageFreshness.tone}:${pageFreshness.value}` : "none"}</output>
      {strips.map((strip) => <Strip key={strip.key} tone={strip.tone} value={strip.value} />)}
    </PageTrustProvider>
  );
}

describe("page trust registry", () => {
  it("reports the worst freshness on the page and forgets a strip when it unmounts", () => {
    const { rerender } = render(<Shell strips={[{ key: "a", tone: "ready", value: "2s ago" }]} />);
    expect(screen.getByTestId("worst")).toHaveTextContent("ready:2s ago");

    rerender(<Shell strips={[
      { key: "a", tone: "ready", value: "2s ago" },
      { key: "b", tone: "review", value: "Stale · 187d ago" }
    ]} />);
    expect(screen.getByTestId("worst")).toHaveTextContent("review:Stale · 187d ago");

    // A route change unmounts the previous page's strips; nothing stale is left for the next page.
    act(() => rerender(<Shell strips={[]} />));
    expect(screen.getByTestId("worst")).toHaveTextContent("none");
  });

  it("ranks blocked over review over unknown over ready", () => {
    expect(selectWorstPageFreshness([
      { tone: "ready", value: "a" },
      { tone: "blocked", value: "b" },
      { tone: "review", value: "c" },
      { tone: "unknown", value: "d" }
    ])).toEqual({ tone: "blocked", value: "b" });
    expect(selectWorstPageFreshness([{ tone: "unknown", value: null }, { tone: "ready", value: "a" }])?.tone).toBe("unknown");
    expect(selectWorstPageFreshness([])).toBeNull();
  });

  it("does nothing outside the shell", () => {
    expect(() => render(<Strip tone="review" value="Stale" />)).not.toThrow();
  });
});
