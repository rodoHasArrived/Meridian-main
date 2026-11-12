import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DensityToggle } from "@/components/ui/density-toggle";

afterEach(() => {
  document.body.removeAttribute("data-theme-density");
  localStorage.clear();
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

  it("persists Terminal and restores it when the control remounts", async () => {
    const user = userEvent.setup();
    const view = render(<DensityToggle persist="meridian.workstation.density.v1" />);
    await user.click(screen.getByRole("radio", { name: "Terminal" }));
    expect(document.body).toHaveAttribute("data-theme-density", "terminal");
    expect(localStorage.getItem("meridian.workstation.density.v1")).toBe("terminal");
    view.unmount();
    render(<DensityToggle persist="meridian.workstation.density.v1" />);
    expect(screen.getByRole("radio", { name: "Terminal" })).toHaveAttribute("aria-checked", "true");
  });
});
