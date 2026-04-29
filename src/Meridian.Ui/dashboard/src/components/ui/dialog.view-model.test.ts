import {
  getFocusableDialogElements,
  resolveDialogTabTarget,
  resolveInitialDialogFocus,
  shouldCloseDialogFromBackdrop,
  shouldCloseDialogFromKey
} from "@/components/ui/dialog.view-model";

describe("dialog interaction view model", () => {
  it("derives close intent from Escape and backdrop targets", () => {
    const backdrop = document.createElement("div");
    const content = document.createElement("div");
    backdrop.append(content);

    expect(shouldCloseDialogFromKey("Escape")).toBe(true);
    expect(shouldCloseDialogFromKey("Enter")).toBe(false);
    expect(shouldCloseDialogFromBackdrop(backdrop, backdrop)).toBe(true);
    expect(shouldCloseDialogFromBackdrop(content, backdrop)).toBe(false);
  });

  it("resolves initial focus and tab wrap targets inside the dialog", () => {
    const root = document.createElement("div");
    root.innerHTML = `
      <div role="dialog" tabindex="-1">
        <button id="first">First action</button>
        <button id="disabled" disabled>Disabled action</button>
        <button id="last">Last action</button>
      </div>
    `;
    document.body.append(root);

    const first = root.querySelector<HTMLElement>("#first");
    const last = root.querySelector<HTMLElement>("#last");

    expect(first).not.toBeNull();
    expect(last).not.toBeNull();
    expect(getFocusableDialogElements(root).map((element) => element.id)).toEqual(["first", "last"]);
    expect(resolveInitialDialogFocus(root)).toBe(first);
    expect(resolveDialogTabTarget(root, first, true)).toBe(last);
    expect(resolveDialogTabTarget(root, last, false)).toBe(first);
    expect(resolveDialogTabTarget(root, first, false)).toBeNull();

    root.remove();
  });
});
