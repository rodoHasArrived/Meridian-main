import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { CommandPalette } from "@/components/meridian/command-palette";

describe("CommandPalette", () => {
  it("opens from keyboard shortcut", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    render(
      <MemoryRouter>
        <CommandPalette open={false} onOpenChange={onOpenChange} />
      </MemoryRouter>
    );

    await user.keyboard("{Control>}k{/Control}");

    expect(onOpenChange).toHaveBeenCalledWith(true);
  });

  it("renders workspace navigation items", () => {
    render(
      <MemoryRouter>
        <CommandPalette open={true} onOpenChange={() => undefined} />
      </MemoryRouter>
    );

    expect(screen.getByText("Research")).toBeInTheDocument();
    expect(screen.getByText("Trading")).toBeInTheDocument();
  });
});
