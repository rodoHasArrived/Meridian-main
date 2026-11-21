import { render, screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { LocalDateText } from "@/components/ui/local-date-text";

const EM_DASH = "—";

describe("LocalDateText", () => {
  it("renders a calendar date as a time element carrying the ISO day", () => {
    render(<LocalDateText value="2026-06-30" />);

    const time = screen.getByText("2026-06-30");
    expect(time.tagName).toBe("TIME");
    expect(time).toHaveAttribute("datetime", "2026-06-30");
  });

  it("keeps the machine day ISO whatever the visible format", () => {
    const { rerender } = render(<LocalDateText value="2026-06-30" format="long" />);
    const long = screen.getByText("Tuesday, June 30, 2026");
    expect(long.tagName).toBe("TIME");
    expect(long).toHaveAttribute("datetime", "2026-06-30");

    rerender(<LocalDateText value="2026-06-30" format="month" />);
    const month = screen.getByText("June 2026");
    expect(month).toHaveAttribute("datetime", "2026-06-30");
  });

  it("accepts a local Date", () => {
    render(<LocalDateText value={new Date(2026, 5, 30, 23, 59)} />);

    expect(screen.getByText("2026-06-30")).toHaveAttribute("datetime", "2026-06-30");
  });

  it("renders an em dash in a plain span when no real date is supplied", () => {
    for (const value of [null, undefined, "", "2026-02-30", "2026-06-30T00:00:00Z", "Not supplied", new Date(Number.NaN)]) {
      const { container, unmount } = render(<LocalDateText value={value} data-testid="absent" />);
      const absent = screen.getByTestId("absent");
      expect(absent.tagName).toBe("SPAN");
      expect(absent).toHaveTextContent(EM_DASH);
      expect(absent).not.toHaveAttribute("datetime");
      expect(container.querySelector("time")).toBeNull();
      unmount();
    }
  });

  it("prints the caller's fallback verbatim instead of the em dash", () => {
    const { container } = render(<LocalDateText value="Not supplied" fallback="Not supplied" className="font-mono" />);

    const fallback = screen.getByText("Not supplied");
    expect(fallback.tagName).toBe("SPAN");
    expect(fallback).toHaveClass("font-mono");
    expect(container.querySelector("time")).toBeNull();
    expect(container).not.toHaveTextContent(EM_DASH);
  });

  it("forwards presentation and accessibility attributes in both branches", () => {
    const { rerender } = render(
      <LocalDateText value="2026-06-30" className="font-mono" title="Ex-date" data-testid="date" aria-label="Ex-date 2026-06-30" />
    );
    const time = screen.getByTestId("date");
    expect(time).toHaveClass("font-mono");
    expect(time).toHaveAttribute("title", "Ex-date");
    expect(time).toHaveAccessibleName("Ex-date 2026-06-30");

    rerender(<LocalDateText value={null} className="font-mono" data-testid="date" aria-label="Ex-date not supplied" />);
    const absent = screen.getByTestId("date");
    expect(absent.tagName).toBe("SPAN");
    expect(absent).toHaveClass("font-mono");
    expect(absent).toHaveAttribute("aria-label", "Ex-date not supplied");
  });

  it("does not let a caller override the machine day", () => {
    const smuggled = { dateTime: "1999-01-01" } as Record<string, string>;
    render(<LocalDateText value="2026-06-30" {...smuggled} />);

    expect(screen.getByText("2026-06-30")).toHaveAttribute("datetime", "2026-06-30");
  });

  it("has no accessibility violations for a date, a spelled-out date and an absent date", async () => {
    const { container } = render(
      <dl>
        <div>
          <dt>Ex-date</dt>
          <dd><LocalDateText value="2026-06-30" /></dd>
        </div>
        <div>
          <dt>Record date</dt>
          <dd><LocalDateText value="2026-07-01" format="long" /></dd>
        </div>
        <div>
          <dt>Payable date</dt>
          <dd><LocalDateText value={null} /></dd>
        </div>
        <div>
          <dt>Period</dt>
          <dd><LocalDateText value="2026-07-01" format="month" /></dd>
        </div>
      </dl>
    );

    expect((await axe(container)).violations).toEqual([]);
  });
});

describe.each(["America/New_York", "Asia/Tokyo"])("LocalDateText in %s", (zone) => {
  const originalTimeZone = process.env.TZ;

  beforeAll(() => {
    process.env.TZ = zone;
  });
  afterAll(() => {
    if (originalTimeZone === undefined) {
      delete process.env.TZ;
    } else {
      process.env.TZ = originalTimeZone;
    }
  });

  it("shows the day it was given, in every format, off Greenwich", () => {
    expect(new Date("2026-01-15T12:00:00Z").getTimezoneOffset()).not.toBe(0);

    const { rerender } = render(<LocalDateText value="2026-01-01" />);
    expect(screen.getByText("2026-01-01")).toHaveAttribute("datetime", "2026-01-01");

    rerender(<LocalDateText value="2026-01-01" format="long" />);
    expect(screen.getByText("Thursday, January 1, 2026")).toHaveAttribute("datetime", "2026-01-01");

    rerender(<LocalDateText value="2026-01-01" format="month" />);
    expect(screen.getByText("January 2026")).toHaveAttribute("datetime", "2026-01-01");
  });
});
