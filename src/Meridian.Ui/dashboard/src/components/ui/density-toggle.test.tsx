import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DensityToggle } from "@/components/ui/density-toggle";

afterEach(() => {
  document.body.removeAttribute("data-theme-density");
});

describe("DensityToggle", () => {
  it("writes the density attribute to the body and reports changes", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<DensityToggle onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "Compact" }));
    expect(onChange).toHaveBeenCalledWith("compact");
    expect(document.body.getAttribute("data-theme-density")).toBe("compact");

    await user.click(screen.getByRole("radio", { name: "Default" }));
    expect(document.body.hasAttribute("data-theme-density")).toBe(false);
  });

  it("marks the active level via aria-checked", () => {
    render(<DensityToggle value="spacious" apply={false} />);
    expect(screen.getByRole("radio", { name: "Spacious" })).toHaveAttribute("aria-checked", "true");
  });

  it("keeps one tab stop and supports wrapped arrows, Home and End without shrinking type", async () => {
    const user = userEvent.setup();
    render(<DensityToggle showLabels={false} />);
    const compact = screen.getByRole("radio", { name: "Compact" });
    const normal = screen.getByRole("radio", { name: "Default" });
    const spacious = screen.getByRole("radio", { name: "Spacious" });
    expect(compact).toHaveAttribute("tabindex", "-1");
    await user.tab();
    expect(normal).toHaveFocus();
    await user.keyboard("{ArrowRight}");
    expect(spacious).toHaveFocus();
    expect(spacious).toHaveAttribute("aria-checked", "true");
    await user.keyboard("{ArrowDown}");
    expect(compact).toHaveFocus();
    expect(document.body).toHaveAttribute("data-theme-density", "compact");
    await user.keyboard("{End}");
    expect(spacious).toHaveFocus();
    await user.keyboard("{Home}");
    expect(compact).toHaveFocus();
    expect(normal).toHaveAttribute("tabindex", "-1");
  });
});
