import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { EditableCell } from "./editable-cell";

const wholeNumber = (draft: string) => (/^[0-9]+$/.test(draft) ? true : "Digits only");

describe("EditableCell (Concrete)", () => {
  it("shows the value as a named button and an em dash when there is none", () => {
    const { rerender } = render(<EditableCell value="a.patel" onCommit={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Edit a.patel" })).toHaveTextContent("a.patel");

    rerender(<EditableCell value={null} onCommit={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Edit empty value" })).toHaveTextContent("—");
  });

  it("opens on click, Enter and Space, with the value selected in the input", async () => {
    const user = userEvent.setup();
    render(<EditableCell value="250" onCommit={vi.fn()} ariaLabel="Edit budget" />);

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    const input = screen.getByRole("textbox", { name: "Edit budget" });
    expect(input).toHaveFocus();
    expect(input).toHaveValue("250");
    await user.keyboard("{Escape}");

    screen.getByRole("button", { name: "Edit budget" }).focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("textbox", { name: "Edit budget" })).toHaveFocus();
    await user.keyboard("{Escape}");

    await user.keyboard(" ");
    expect(screen.getByRole("textbox", { name: "Edit budget" })).toHaveFocus();
  });

  it("commits the draft as a string on Enter and returns focus to the cell", async () => {
    const user = userEvent.setup();
    const onCommit = vi.fn();
    const onChange = vi.fn();
    render(<EditableCell value={250} mode="number" onCommit={onCommit} onChange={onChange} ariaLabel="Edit budget" />);

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    const input = screen.getByRole("spinbutton", { name: "Edit budget" });
    await user.clear(input);
    await user.type(input, "300{Enter}");

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith("300");
    expect(onChange).toHaveBeenLastCalledWith("300");
    expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit budget" })).toHaveFocus();
  });

  it("commits once when Tab is followed by its own blur", async () => {
    const onCommit = vi.fn();
    render(<EditableCell value="a.patel" onCommit={onCommit} ariaLabel="Edit owner" />);

    fireEvent.click(screen.getByRole("button", { name: "Edit owner" }));
    const input = screen.getByRole("textbox", { name: "Edit owner" });
    fireEvent.change(input, { target: { value: "m.chen" } });
    fireEvent.keyDown(input, { key: "Tab" });
    fireEvent.blur(input);

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith("m.chen");
  });

  it("commits on blur", async () => {
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(
      <>
        <EditableCell value="a.patel" onCommit={onCommit} ariaLabel="Edit owner" />
        <button type="button">Elsewhere</button>
      </>
    );

    await user.click(screen.getByRole("button", { name: "Edit owner" }));
    await user.clear(screen.getByRole("textbox", { name: "Edit owner" }));
    await user.type(screen.getByRole("textbox", { name: "Edit owner" }), "r.okafor");
    await user.click(screen.getByRole("button", { name: "Elsewhere" }));

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith("r.okafor");
  });

  it("keeps the editor open and announces the validator's message when the draft is refused", async () => {
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(
      <EditableCell value="250" onCommit={onCommit} validator={wholeNumber} hint="Enter commits" ariaLabel="Edit budget" />
    );

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    const input = screen.getByRole("textbox", { name: "Edit budget" });
    expect(input).toHaveAccessibleDescription("Enter commits");

    await user.clear(input);
    await user.type(input, "2x{Enter}");

    expect(onCommit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("Digits only");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Digits only");

    await user.type(input, "{Backspace}");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(input).not.toHaveAttribute("aria-invalid");
    await user.keyboard("{Enter}");
    expect(onCommit).toHaveBeenCalledWith("2");
  });

  it("cancels on Escape without committing, without closing an enclosing surface, and restores the value", async () => {
    const user = userEvent.setup();
    const onCommit = vi.fn();
    const onSurfaceKeyDown = vi.fn();
    render(
      // Stands in for a drawer's Escape handler.
      <div onKeyDown={onSurfaceKeyDown}>
        <EditableCell value="250" onCommit={onCommit} ariaLabel="Edit budget" />
      </div>
    );

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    await user.clear(screen.getByRole("textbox", { name: "Edit budget" }));
    await user.type(screen.getByRole("textbox", { name: "Edit budget" }), "999");
    onSurfaceKeyDown.mockClear();
    await user.keyboard("{Escape}");

    expect(onCommit).not.toHaveBeenCalled();
    expect(onSurfaceKeyDown).not.toHaveBeenCalled();
    const display = screen.getByRole("button", { name: "Edit budget" });
    expect(display).toHaveTextContent("250");
    expect(display).toHaveFocus();
  });

  it("re-reads the value each time the editor opens", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<EditableCell value="250" onCommit={vi.fn()} ariaLabel="Edit budget" />);

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    await user.type(screen.getByRole("textbox", { name: "Edit budget" }), "9{Enter}");
    rerender(<EditableCell value="400" onCommit={vi.fn()} ariaLabel="Edit budget" />);
    await user.click(screen.getByRole("button", { name: "Edit budget" }));

    expect(screen.getByRole("textbox", { name: "Edit budget" })).toHaveValue("400");
  });

  it("cannot be opened while disabled and says so to assistive technology", async () => {
    const user = userEvent.setup();
    render(<EditableCell value="250" onCommit={vi.fn()} disabled ariaLabel="Edit budget" />);

    const display = screen.getByRole("button", { name: "Edit budget" });
    expect(display).toHaveAttribute("aria-disabled", "true");
    expect(display).toHaveAttribute("tabindex", "-1");
    await user.click(display);
    fireEvent.keyDown(display, { key: "Enter" });

    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("has no axe violations at rest, while editing, and with a validation error", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <EditableCell value="250" onCommit={vi.fn()} validator={wholeNumber} hint="Enter commits" ariaLabel="Edit budget" />
    );
    expect((await axe(container)).violations).toEqual([]);

    await user.click(screen.getByRole("button", { name: "Edit budget" }));
    expect((await axe(container)).violations).toEqual([]);

    await user.type(screen.getByRole("textbox", { name: "Edit budget" }), "x{Enter}");
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps the display at least 24px wide as well as tall, so a one-character value is still a full target", () => {
    render(<EditableCell value="5" onCommit={vi.fn()} />);

    const css = document.head.querySelector('style[data-mds="editable-cell"]')?.textContent ?? "";
    const display = /\.mds-editable__display\{([^}]*)\}/.exec(css)?.[1] ?? "";
    expect(display).toContain("min-width:24px");
    expect(display).toContain("min-height:24px");
    expect(display).toContain("box-sizing:border-box");
  });
});
