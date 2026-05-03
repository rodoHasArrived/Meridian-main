using System.Collections.Generic;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Meridian.Wpf.Controls;

/// <summary>
/// A <see cref="Border"/> whose automation peer reports no children, preventing
/// Windows UI Automation descendant-scope tree walks from traversing into its
/// visual subtree.
/// </summary>
/// <remarks>
/// Use this wrapper around controls whose automation providers can stall a
/// <c>FindFirst(TreeScope.Descendants)</c> query. AvalonEdit's
/// <c>TextEditor</c> is a known example: its visual subtree lacks a proper UIA
/// automation peer, which causes the PowerShell shell-readiness poll in
/// <c>run-desktop-workflow.ps1</c> to block indefinitely when the QuantScript
/// page is active.
/// </remarks>
public sealed class AutomationLeafBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new AutomationLeafBorderPeer(this);
}

internal sealed class AutomationLeafBorderPeer : FrameworkElementAutomationPeer
{
    internal AutomationLeafBorderPeer(FrameworkElement owner) : base(owner) { }

    /// <summary>Returns null so UIA does not enumerate children of this subtree.</summary>
    protected override List<AutomationPeer>? GetChildrenCore() => null;

    /// <summary>Excluded from the Control view — only present in the Raw view.</summary>
    protected override bool IsControlElementCore() => false;

    /// <summary>Excluded from the Content view — only present in the Raw view.</summary>
    protected override bool IsContentElementCore() => false;
}
