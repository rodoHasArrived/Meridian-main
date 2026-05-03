export interface ButtonCommandOptions {
  disabled?: boolean;
  busy?: boolean;
  busyLabel?: string | null;
  disabledReason?: string | null;
  title?: string;
}

export interface ButtonCommandViewModel {
  disabled: boolean;
  ariaBusy: true | undefined;
  ariaDisabled: true | undefined;
  title: string | undefined;
  displayBusyLabel: string | null;
  showBusyIndicator: boolean;
  iconAriaHidden: true;
}

export function buildButtonCommandViewModel({
  disabled = false,
  busy = false,
  busyLabel = null,
  disabledReason = null,
  title
}: ButtonCommandOptions = {}): ButtonCommandViewModel {
  const commandDisabled = disabled || busy;

  return {
    disabled: commandDisabled,
    ariaBusy: busy ? true : undefined,
    ariaDisabled: commandDisabled ? true : undefined,
    title: busy && busyLabel ? busyLabel : commandDisabled && disabledReason ? disabledReason : title,
    displayBusyLabel: busy && busyLabel ? busyLabel : null,
    showBusyIndicator: busy,
    iconAriaHidden: true
  };
}
