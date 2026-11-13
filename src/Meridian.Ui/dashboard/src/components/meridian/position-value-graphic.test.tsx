import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PositionValueGraphic } from "@/components/meridian/position-value-graphic";
import { measureDisplayedPositionAmounts } from "@/lib/position-value-graphic";

describe("PositionValueGraphic", () => {
  it("keeps original loss text and draws it left from the zero baseline", () => {
    const { container } = render(<PositionValueGraphic value="-$100" kind="pnl" measurement={measureDisplayedPositionAmounts(["-$100", "+$200"])[0]} />);
    expect(screen.getByText("-$100")).toBeInTheDocument();
    const track = container.querySelector('[data-position-value-graphic="pnl"]');
    expect(track?.lastElementChild).toHaveStyle({ width: "25%", left: "25%" });
    expect(screen.getByText(/P&L magnitude: 50% of largest displayed/)).toBeInTheDocument();
  });

  it("identifies the largest displayed exposure needing server-assessed mark review", () => {
    render(<PositionValueGraphic value="$18,900" kind="exposure" reviewRequired measurement={measureDisplayedPositionAmounts(["$18,900"])[0]} />);
    expect(screen.getByText("$18,900")).toBeInTheDocument();
    expect(screen.getByText("Largest · mark review")).toBeInTheDocument();
  });

  it("labels explicit source currency and unavailable currency without pooling them", () => {
    const { rerender } = render(<PositionValueGraphic value="$100" kind="exposure" measurement={measureDisplayedPositionAmounts(["$100"], ["USD"])[0]} />);
    expect(screen.getByText(/largest displayed USD \(\$\) exposure/)).toBeInTheDocument();
    rerender(<PositionValueGraphic value="$100" kind="exposure" measurement={measureDisplayedPositionAmounts(["$100"], [null])[0]} />);
    expect(screen.getByText(/largest displayed \$ \(currency unavailable\) exposure/)).toBeInTheDocument();
  });

  it("preserves an unsupported original amount without drawing a zero or assuming currency", () => {
    const { container } = render(<PositionValueGraphic value="EUR 1.234,56" kind="exposure" measurement={null} />);
    expect(screen.getByText("EUR 1.234,56")).toBeInTheDocument();
    expect(screen.getByText("Graphic unavailable")).toBeInTheDocument();
    expect(container.querySelector("[data-position-value-graphic]")).toBeNull();
  });
});
