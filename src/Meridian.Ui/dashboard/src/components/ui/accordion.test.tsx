import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { Accordion } from "@/components/ui/accordion";

const items = [
  { title: "Section A", content: "Body A" },
  { title: "Section B", content: "Body B" },
  { title: "Section C", content: "Body C" }
];

describe("Accordion", () => {
  it("opens a section on click and collapses others in single mode", async () => {
    const user = userEvent.setup();
    render(<Accordion items={items} />);

    await user.click(screen.getByRole("button", { name: /Section A/ }));
    expect(screen.getByText("Body A")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Section B/ }));
    expect(screen.queryByText("Body A")).not.toBeInTheDocument();
    expect(screen.getByText("Body B")).toBeInTheDocument();
  });

  it("keeps multiple sections open in multi mode", async () => {
    const user = userEvent.setup();
    render(<Accordion items={items} multi />);

    await user.click(screen.getByRole("button", { name: /Section A/ }));
    await user.click(screen.getByRole("button", { name: /Section B/ }));

    expect(screen.getByText("Body A")).toBeInTheDocument();
    expect(screen.getByText("Body B")).toBeInTheDocument();
  });

  it("moves header focus with arrow keys", async () => {
    const user = userEvent.setup();
    render(<Accordion items={items} />);

    screen.getByRole("button", { name: /Section A/ }).focus();
    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("button", { name: /Section B/ })).toHaveFocus();
  });
});
