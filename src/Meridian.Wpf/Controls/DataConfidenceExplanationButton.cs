using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Meridian.Wpf.Controls;

/// <summary>
/// Button whose automation peer stops advertising an interactive Button/Invoke role while
/// no command is bound: a read-only confidence badge must not present assistive technology
/// with an action that does nothing. The click-through trigger nulls the command too, so
/// the same check covers both read-only states.
/// </summary>
public sealed class DataConfidenceExplanationButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new DataConfidenceExplanationButtonAutomationPeer(this);

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
