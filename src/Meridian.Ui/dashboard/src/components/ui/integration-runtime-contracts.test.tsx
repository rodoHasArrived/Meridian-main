import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DensityToggle } from "@/components/ui/density-toggle";
import { RadioGroup } from "@/components/ui/radio-group";
import { getFocusableDialogElements, resolveInitialDialogFocus, resolveDialogTabTarget } from "@/components/ui/dialog.view-model";

afterEach(() => vi.restoreAllMocks());

describe("native adapter integration contracts", () => {
  it("moves density selection and its single tab stop together, including Home and End", async () => {
    const user = userEvent.setup();
    render(<DensityToggle apply={false} />);
    const compact = screen.getByRole("radio", { name: "Compact" });
    const standard = screen.getByRole("radio", { name: "Default" });
    const spacious = screen.getByRole("radio", { name: "Spacious" });
    expect(screen.getAllByRole("radio").filter((item) => item.tabIndex === 0)).toEqual([standard]);
    standard.focus(); await user.keyboard("{ArrowRight}");
    expect(spacious).toHaveFocus(); expect(spacious).toHaveAttribute("aria-checked", "true");
    await user.keyboard("{Home}"); expect(compact).toHaveFocus();
    await user.keyboard("{End}"); expect(spacious).toHaveFocus();
    expect(screen.getAllByRole("radio").filter((item) => item.tabIndex === 0)).toEqual([spacious]);
  });

  it("keeps density usable when storage access is denied", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new DOMException("Denied", "SecurityError"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new DOMException("Denied", "SecurityError"); });
    const user = userEvent.setup(); const changed = vi.fn();
    render(<DensityToggle apply={false} persist="denied-density" onChange={changed} />);
    await user.click(screen.getByRole("radio", { name: "Compact" }));
    expect(changed).toHaveBeenLastCalledWith("compact");
    expect(screen.getByRole("radio", { name: "Compact" })).toHaveAttribute("aria-checked", "true");
  });

  it("lets radio Home/End skip disabled entries and move actual focus", async () => {
    const user = userEvent.setup();
    function Controlled() {
      const [value, setValue] = useState("review");
      return <RadioGroup aria-label="Evidence filter" value={value} onChange={setValue} options={[{ value: "locked", label: "Locked", disabled: true }, { value: "review", label: "Review" }, { value: "verified", label: "Verified" }]} />;
    }
    render(<Controlled />);
    screen.getByRole("radio", { name: "Review" }).focus();
    await user.keyboard("{End}"); expect(screen.getByRole("radio", { name: "Verified" })).toHaveFocus();
    await user.keyboard("{Home}"); expect(screen.getByRole("radio", { name: "Review" })).toHaveFocus();
    expect(screen.getAllByRole("radio").filter((item) => item.tabIndex === 0)).toHaveLength(1);
  });

  it("filters hidden ancestors and closed details while separating initial heading focus from Tab cycling", () => {
    const root = document.createElement("div");
    root.innerHTML = `<div role="dialog" tabindex="-1">
      <h2 tabindex="-1" data-dialog-autofocus id="heading">Review evidence</h2>
      <div hidden><button>Hidden</button></div><div inert><button>Inert</button></div>
      <fieldset disabled><button>Disabled by fieldset</button></fieldset>
      <div style="display:none"><button>CSS hidden</button></div>
      <details><summary id="summary">Details</summary><button>Collapsed</button></details>
      <button style="position:fixed" id="fixed">Fixed action</button><button id="last">Last action</button>
    </div>`;
    document.body.append(root);
    try {
      const heading = root.querySelector<HTMLElement>("#heading")!;
      const last = root.querySelector<HTMLElement>("#last")!;
      expect(resolveInitialDialogFocus(root)).toBe(heading);
      expect(getFocusableDialogElements(root).map((item) => item.id)).toEqual(["summary", "fixed", "last"]);
      expect(resolveDialogTabTarget(root, heading, false)?.id).toBe("summary");
      expect(resolveDialogTabTarget(root, heading, true)).toBe(last);
    } finally { root.remove(); }
  });
});
