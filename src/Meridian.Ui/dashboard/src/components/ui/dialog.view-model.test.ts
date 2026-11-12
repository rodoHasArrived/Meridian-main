import {
  buildDialogCloseButtonViewModel,
  buildDialogContentViewModel,
  getFocusableDialogElements,
  resolveDialogTabTarget,
  resolveInitialDialogFocus,
  shouldCloseDialogFromBackdrop,
  shouldCloseDialogFromKey
} from "@/components/ui/dialog.view-model";

describe("dialog interaction view model", () => {
  it("derives viewport-safe dialog surface presentation", () => {
    const viewModel = buildDialogContentViewModel();

    expect(viewModel.role).toBe("dialog");
    expect(viewModel.ariaModal).toBe(true);
    expect(viewModel.tabIndex).toBe(-1);
    expect(viewModel.className).toContain("max-h-[calc(100dvh-2rem)]");
    expect(viewModel.className).toContain("overflow-y-auto");
    expect(viewModel.className).toContain("overscroll-contain");
    expect(viewModel.className).toContain("rounded-[2px]");
  });

  it("preserves caller-provided tab target while deriving the dialog surface", () => {
    expect(buildDialogContentViewModel(0).tabIndex).toBe(0);
  });

  it("derives accessible close-button semantics", () => {
    expect(buildDialogCloseButtonViewModel()).toMatchObject({
      type: "button",
      ariaLabel: "Close dialog",
      title: "Close dialog",
      disabled: false,
      iconAriaHidden: true
    });

    expect(buildDialogCloseButtonViewModel({
      label: "Close backfill dialog",
      disabled: true,
      disabledReason: "Backfill request is running."
    })).toMatchObject({
      ariaLabel: "Close backfill dialog",
      title: "Backfill request is running.",
      disabled: true
    });
  });

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

  it("excludes hidden and inert descendants and controls disabled by a fieldset", () => {
    const root = document.createElement("div");
    root.innerHTML = `
      <div hidden><button>Hidden</button></div>
      <div inert><button>Inert</button></div>
      <div aria-hidden="true"><button>Not exposed</button></div>
      <div style="display:none"><button>Not rendered</button></div>
      <div style="visibility:hidden"><button>Not visible</button></div>
      <div style="content-visibility:hidden"><button>Hidden content</button></div>
      <fieldset disabled>
        <legend><button id="legend">Legend action</button></legend>
        <button>Disabled by fieldset</button>
      </fieldset>
      <button id="visible">Visible action</button>
    `;
    document.body.append(root);

    expect(getFocusableDialogElements(root).map((element) => element.id)).toEqual(["legend", "visible"]);
    expect(resolveInitialDialogFocus(root)).toBe(root.querySelector("#legend"));
    root.remove();
  });

  it("keeps a closed disclosure summary tabbable while withholding its content", () => {
    const root = document.createElement("div");
    root.innerHTML = `
      <button id="first">First action</button>
      <details>
        <summary id="summary">Evidence details</summary>
        <button id="evidence">Open retained evidence</button>
      </details>
    `;
    document.body.append(root);
    const first = root.querySelector<HTMLElement>("#first");
    const summary = root.querySelector<HTMLElement>("#summary");

    expect(getFocusableDialogElements(root).map((element) => element.id)).toEqual(["first", "summary"]);
    expect(resolveDialogTabTarget(root, first, true)).toBe(summary);
    expect(resolveDialogTabTarget(root, summary, false)).toBe(first);

    root.querySelector("details")!.open = true;
    expect(getFocusableDialogElements(root).map((element) => element.id)).toEqual(["first", "summary", "evidence"]);
    root.remove();
  });
});
