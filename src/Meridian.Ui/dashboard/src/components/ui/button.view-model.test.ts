import { buildButtonCommandViewModel } from "@/components/ui/button.view-model";

describe("button command view model", () => {
  it("keeps enabled commands interactive by default", () => {
    expect(buildButtonCommandViewModel()).toEqual({
      disabled: false,
      ariaBusy: undefined,
      ariaDisabled: undefined,
      title: undefined,
      displayBusyLabel: null,
      showBusyIndicator: false,
      iconAriaHidden: true
    });
  });

  it("derives disabled command title and ARIA state from a reason", () => {
    const viewModel = buildButtonCommandViewModel({
      disabled: true,
      disabledReason: "Preview the request before running."
    });

    expect(viewModel.disabled).toBe(true);
    expect(viewModel.ariaDisabled).toBe(true);
    expect(viewModel.title).toBe("Preview the request before running.");
    expect(viewModel.showBusyIndicator).toBe(false);
  });

  it("derives busy command state and visible busy label", () => {
    const viewModel = buildButtonCommandViewModel({
      busy: true,
      busyLabel: "Previewing..."
    });

    expect(viewModel.disabled).toBe(true);
    expect(viewModel.ariaBusy).toBe(true);
    expect(viewModel.ariaDisabled).toBe(true);
    expect(viewModel.title).toBe("Previewing...");
    expect(viewModel.displayBusyLabel).toBe("Previewing...");
    expect(viewModel.showBusyIndicator).toBe(true);
  });
});
