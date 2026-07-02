import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ThemeToggle } from "@/components/ui/theme-toggle";
import { APPEARANCE_STORAGE_KEY } from "@/lib/theme";

afterEach(() => {
  document.documentElement.removeAttribute("data-theme");
  localStorage.clear();
});

describe("ThemeToggle", () => {
  it("writes the theme attribute and reports changes", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<ThemeToggle onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "Dark" }));
    expect(onChange).toHaveBeenCalledWith("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(localStorage.getItem(APPEARANCE_STORAGE_KEY)).toBe("dark");

    await user.click(screen.getByRole("radio", { name: "System" }));
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    expect(localStorage.getItem(APPEARANCE_STORAGE_KEY)).toBe("system");
  });

  it("marks the active appearance via aria-checked", () => {
    render(<ThemeToggle value="light" apply={false} />);
    expect(screen.getByRole("radio", { name: "Light" })).toHaveAttribute("aria-checked", "true");
  });
});
