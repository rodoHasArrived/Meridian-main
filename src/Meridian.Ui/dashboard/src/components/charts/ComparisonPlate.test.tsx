import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import {
  ComparisonPlate,
  PLATE_PLOT_HEIGHT,
  PLATE_VIEW_WIDTH,
  PlateGrid,
  plateDomain,
  plateGutters,
  platePath,
  plateScale,
  plateTicks,
  type PlateSpec
} from "./ComparisonPlate";

const categories = ["start", "", "end"];
const plates: PlateSpec[] = [
  { id: "a", title: "Alpha", tag: "PAPER", values: [100, 104, 110], footerLabel: "Total return", footerValue: "+10.00%" },
  { id: "b", title: "Beta", tag: "BACKTEST", values: [100, 98, 95], footerLabel: "Total return", footerValue: "-5.00%" }
];

function tickLabels(plate: HTMLElement): string[] {
  const svg = plate.querySelector("svg");
  // Axis tick labels are the texts anchored at x=0; category and end labels sit elsewhere.
  return Array.from(svg?.querySelectorAll("text[x='0']") ?? []).map((node) => node.textContent ?? "");
}

/** Tick labels of a figure found by its `img` role (the svg itself). */
function figureTickLabels(figure: Element): string[] {
  return Array.from(figure.querySelectorAll("text[x='0']")).map((node) => node.textContent ?? "");
}

function gridlineYs(plate: HTMLElement): string[] {
  return Array.from(plate.querySelectorAll("svg line")).map((node) => node.getAttribute("y1") ?? "");
}

describe("plate kernel helpers", () => {
  it("pads one domain across every series and ignores gaps", () => {
    const [min, max] = plateDomain([[100, null, 110], [90, 95]]);

    expect(min).toBeCloseTo(90 - 20 * 0.08, 10);
    expect(max).toBeCloseTo(110 + 20 * 0.08, 10);
  });

  it("falls back to a unit domain when nothing is plottable and pads a flat series", () => {
    expect(plateDomain([[null], []])).toEqual([0, 1]);

    const [min, max] = plateDomain([[100, 100]]);
    expect(min).toBeLessThan(100);
    expect(max).toBeGreaterThan(100);
  });

  it("produces round ticks inside the domain", () => {
    expect(plateTicks(93.4, 111.6, 4)).toEqual([95, 100, 105, 110]);
  });

  it("keeps one tick for a degenerate domain and none for a non-finite one", () => {
    expect(plateTicks(100, 100, 4)).toEqual([100]);
    expect(plateTicks(Number.NaN, 1, 4)).toEqual([]);
  });

  it("reports the step between the shared ticks, free of float noise", () => {
    expect(plateScale([93.4, 111.6])).toEqual({ domain: [93.4, 111.6], ticks: [95, 100, 105, 110], step: 5 });
    expect(plateScale([99.9984, 100.0216]).step).toBe(0.01);
    expect(plateScale([100, 100]).step).toBeNull();
  });

  it("keeps the reference gutters for short labels and widens them for long ones, up to a cap", () => {
    expect(plateGutters(["95", "100", "105"], ["104.2"])).toEqual({ left: 36, right: 46 });

    const wide = plateGutters(["100", "100.05", "100.15"], ["100.15"]);
    // Six characters of the 12px data face need about 44 view-box units.
    expect(wide.left).toBeGreaterThanOrEqual(6 * 7.2 + 4);
    expect(wide.right).toBeGreaterThanOrEqual(5 + 6 * 7.2);

    const capped = plateGutters(["1".repeat(40)], ["1".repeat(40)]);
    expect(PLATE_VIEW_WIDTH - capped.left - capped.right).toBeGreaterThanOrEqual(60);
  });

  it("breaks the line at a missing value instead of bridging it", () => {
    const path = platePath([1, null, 3, 4], (index) => index * 10, (value) => value);

    expect(path).toBe("M0.00 1.00M20.00 3.00L30.00 4.00");
  });
});

describe("ComparisonPlate", () => {
  it("names the figure with its title, footer and first/last values, and shows them without hover", () => {
    render(
      <ComparisonPlate
        title="Alpha"
        tag="PAPER"
        categories={categories}
        values={[100, 104, 110]}
        domain={[90, 115]}
        footerLabel="Total return"
        footerValue="+10.00%"
      />
    );

    const figure = screen.getByRole("img", { name: "Alpha — Total return +10.00%; first 100, last 110" });
    expect(within(figure).getByText("start")).toBeInTheDocument();
    expect(within(figure).getByText("end")).toBeInTheDocument();
    expect(within(figure).getAllByText("110").length).toBeGreaterThan(0);
    expect(screen.getByText("PAPER")).toBeInTheDocument();
    expect(screen.getByText("+10.00%")).toBeInTheDocument();
  });

  it("is a static frame without onSelect and a pressed-state button with it", async () => {
    const onSelect = vi.fn();
    const { rerender } = render(
      <ComparisonPlate title="Alpha" categories={categories} values={[100, 104, 110]} domain={[90, 115]} />
    );
    expect(screen.queryByRole("button")).not.toBeInTheDocument();

    rerender(
      <ComparisonPlate title="Alpha" categories={categories} values={[100, 104, 110]} domain={[90, 115]} onSelect={onSelect} />
    );
    const plate = screen.getByRole("button", { name: /Alpha/ });
    expect(plate).toHaveAttribute("aria-pressed", "false");

    await userEvent.setup().click(plate);
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("carries role by dash and marker as well as colour", () => {
    const { container, rerender } = render(
      <ComparisonPlate title="Actual" role="actual" categories={categories} values={[1, 2, 3]} domain={[0, 4]} />
    );
    expect(container.querySelector("path")).not.toHaveAttribute("stroke-dasharray");
    expect(container.querySelectorAll("circle")).toHaveLength(3);
    expect(container.querySelector("path")?.getAttribute("stroke")).toContain("--chart-series-1");

    rerender(
      <ComparisonPlate title="Benchmark" role="benchmark" categories={categories} values={[1, 2, 3]} domain={[0, 4]} />
    );
    expect(container.querySelector("path")).toHaveAttribute("stroke-dasharray", "4 4");
    expect(container.querySelectorAll("circle")).toHaveLength(0);
    expect(container.querySelector("path")?.getAttribute("stroke")).toContain("--chart-compare-2");

    rerender(
      <ComparisonPlate title="Prior" role="prior" categories={categories} values={[1, 2, 3]} domain={[0, 4]} />
    );
    expect(container.querySelector("path")?.getAttribute("stroke")).toContain("--chart-compare-1");
  });

  it("states why nothing is drawn instead of drawing, and cannot be selected", () => {
    render(
      <ComparisonPlate
        title="Gamma"
        categories={categories}
        values={[]}
        domain={[0, 1]}
        onSelect={() => undefined}
        status={{ kind: "unavailable", text: "Unavailable. No equity curve is published for this run." }}
        footerLabel="Total return"
        footerValue="Not reported"
      />
    );

    expect(screen.getByText("Unavailable. No equity curve is published for this run.")).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("Not reported")).toBeInTheDocument();
  });

  it("uses 12px text in the plot", () => {
    const { container } = render(
      <ComparisonPlate title="Alpha" categories={categories} values={[100, 104, 110]} domain={[90, 115]} />
    );

    for (const text of Array.from(container.querySelectorAll("svg text"))) {
      expect((text as SVGTextElement).style.fontSize).toBe("12px");
    }
  });
});

describe("PlateGrid", () => {
  it("gives every plate the same domain, the same ticks and the same plot size", () => {
    render(<PlateGrid plates={plates} categories={categories} onSelect={() => undefined} />);

    const alpha = screen.getByRole("button", { name: /Alpha/ });
    const beta = screen.getByRole("button", { name: /Beta/ });

    expect(tickLabels(alpha)).toEqual(tickLabels(beta));
    expect(tickLabels(alpha).length).toBeGreaterThan(1);
    expect(gridlineYs(alpha)).toEqual(gridlineYs(beta));
    for (const plate of [alpha, beta]) {
      const svg = plate.querySelector("svg");
      expect(svg).toHaveAttribute("viewBox", `0 0 ${PLATE_VIEW_WIDTH} ${PLATE_PLOT_HEIGHT}`);
      expect(svg).toHaveAttribute("height", String(PLATE_PLOT_HEIGHT));
    }
  });

  it("never rescales per plate: the same value lands on the same y in both plates", () => {
    render(<PlateGrid plates={plates} categories={categories} />);

    const figures = screen.getAllByRole("img");
    const firstPointY = figures.map((figure) => figure.querySelector("circle")?.getAttribute("cy"));

    // Both series start at 100, so their first markers share a y even though their ranges differ.
    expect(firstPointY[0]).toBeDefined();
    expect(firstPointY[0]).toBe(firstPointY[1]);
  });

  it("selects by rail and pressed state without resizing the plate", async () => {
    function Harness() {
      return <PlateGrid plates={plates} categories={categories} selectedId="b" onSelect={() => undefined} />;
    }
    render(<Harness />);

    const alpha = screen.getByRole("button", { name: /Alpha/ });
    const beta = screen.getByRole("button", { name: /Beta/ });

    expect(alpha).toHaveAttribute("aria-pressed", "false");
    expect(beta).toHaveAttribute("aria-pressed", "true");
    expect(beta).toHaveAttribute("data-plate-selected", "true");
    expect(beta.querySelector("svg")?.getAttribute("viewBox")).toBe(alpha.querySelector("svg")?.getAttribute("viewBox"));
    expect(beta.querySelector("svg")?.getAttribute("height")).toBe(alpha.querySelector("svg")?.getAttribute("height"));
  });

  it("keeps a plate without a series in place and out of the shared domain", () => {
    const withUnavailable: PlateSpec[] = [
      ...plates,
      { id: "c", title: "Gamma", values: [], status: { kind: "loading", text: "Loading equity curve…" } }
    ];
    const { container } = render(<PlateGrid plates={withUnavailable} categories={categories} />);
    const { container: reference } = render(<PlateGrid plates={plates} categories={categories} />);

    expect(screen.getByText("Loading equity curve…")).toBeInTheDocument();
    expect(container.firstElementChild?.getAttribute("data-plate-domain"))
      .toBe(reference.firstElementChild?.getAttribute("data-plate-domain"));
  });

  it("hands the formatter the shared scale", () => {
    const format = vi.fn((value: number, scale: { step: number | null }) => `${value}/${scale.step}`);
    render(<PlateGrid plates={plates} categories={categories} format={format} />);

    const [alpha] = screen.getAllByRole("img");
    expect(figureTickLabels(alpha)).toEqual(["95/5", "100/5", "105/5", "110/5"]);
    expect(alpha).toHaveAccessibleName("Alpha — Total return +10.00%; first 100/5, last 110/5");
    expect(format.mock.calls.every(([, scale]) => scale.step === 5)).toBe(true);
  });

  it("widens the label gutters identically in every plate so long labels clear the trace", () => {
    const nearFlat: PlateSpec[] = [
      { id: "a", title: "Alpha", values: [100, 100.07, 100.15] },
      { id: "b", title: "Beta", values: [100, 100.05, 100.1] }
    ];
    render(<PlateGrid plates={nearFlat} categories={categories} format={(value) => value.toFixed(2)} />);

    const [alpha, beta] = screen.getAllByRole("img") as HTMLElement[];
    const labels = figureTickLabels(alpha);
    expect(labels).toEqual(["100.00", "100.05", "100.10", "100.15"]);
    expect(figureTickLabels(beta)).toEqual(labels);
    expect(labels.every((label) => label.length === 6)).toBe(true);

    const markerXs = (figure: HTMLElement) =>
      Array.from(figure.querySelectorAll("circle")).map((node) => Number(node.getAttribute("cx")));
    const gridStart = (figure: HTMLElement) => Number(figure.querySelector("line")?.getAttribute("x1"));
    // Same plot in both plates, starting to the right of a six-character tick label...
    expect(markerXs(alpha)).toEqual(markerXs(beta));
    expect(gridStart(alpha)).toBe(gridStart(beta));
    expect(gridStart(alpha)).toBeGreaterThanOrEqual(6 * 7.2);
    expect(markerXs(alpha)[0]).toBe(gridStart(alpha));
    // ...and ending early enough for a six-character end label inside the view box.
    const lastMarker = markerXs(alpha)[markerXs(alpha).length - 1];
    expect(lastMarker + 5 + 6 * 7.2).toBeLessThanOrEqual(PLATE_VIEW_WIDTH);
  });

  it("honours an explicit shared domain", () => {
    const { container } = render(<PlateGrid plates={plates} categories={categories} domain={[0, 200]} />);

    expect(container.firstElementChild).toHaveAttribute("data-plate-domain", "0,200");
  });

  it("has no basic accessibility violations", async () => {
    const { container } = render(
      <PlateGrid
        plates={[
          ...plates,
          { id: "c", title: "Gamma", values: [], status: { kind: "unavailable", text: "Unavailable. No series." } }
        ]}
        categories={categories}
        selectedId="a"
        onSelect={() => undefined}
      />
    );

    expect((await axe(container)).violations).toEqual([]);
  });
});
