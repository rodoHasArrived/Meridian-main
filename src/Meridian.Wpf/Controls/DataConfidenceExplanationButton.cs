using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace Meridian.Wpf.Controls;

/// <summary>
/// Button whose automation peer stops advertising an interactive Button/Invoke role while
/// no command is bound: a read-only confidence badge must not present assistive technology
/// with an action that does nothing. The click-through trigger nulls the command too, so
/// the same check covers both read-only states. Pointer input follows the same rule: a
/// commandless badge leaves mouse events unhandled so they reach a hosting row or card.
/// </summary>
public sealed class DataConfidenceExplanationButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new DataConfidenceExplanationButtonAutomationPeer(this);

    // While no command is bound, ButtonBase must not run its pointer handling: it would
    // capture the mouse and mark the events handled, swallowing clicks that a hosting
    // selectable row or card needs. The element stays hit-testable so the explanation
    // tooltip still shows.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Command is null)
        {
            return;
        }

        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        // Unless this button still owns a press the base began: the click-through trigger
        // can clear the command between down and up, and skipping the base up would then
        // strand the mouse capture and pressed state on a now-inert badge.
        if (Command is null && !IsPressed && !IsMouseCaptured)
        {
            return;
        }

        base.OnMouseLeftButtonUp(e);
    }

    private sealed class DataConfidenceExplanationButtonAutomationPeer : ButtonAutomationPeer
    {
        public DataConfidenceExplanationButtonAutomationPeer(Button owner)
            : base(owner)
        {
        }

        private bool IsReadOnlyBadge => ((Button)Owner).Command is null;

        protected override AutomationControlType GetAutomationControlTypeCore()
            => IsReadOnlyBadge ? AutomationControlType.Text : base.GetAutomationControlTypeCore();

        public override object? GetPattern(PatternInterface patternInterface)
            => patternInterface == PatternInterface.Invoke && IsReadOnlyBadge
                ? null
                : base.GetPattern(patternInterface);
    }
}
