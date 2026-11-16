using System.Windows.Input;

namespace Meridian.Wpf.Contracts;

/// <summary>
/// Implemented by a page or page view model that can reload its own data in place.
/// The shell Refresh control (toolbar button and F5) executes <see cref="RefreshCommand"/>
/// instead of re-navigating, so selection, filters, and typed input survive a refresh.
/// </summary>
/// <remarks>
/// View models that already expose a public <c>RefreshCommand</c> property are found by convention
/// and do not need to implement this interface; implement it when the reload command has another name.
/// </remarks>
public interface IPageRefreshTarget
{
    ICommand RefreshCommand { get; }
}

/// <summary>
/// Implemented by shell chrome that wraps the page the operator is working in, so the shell
/// refresh reaches the hosted page rather than the wrapper.
/// </summary>
public interface IPageRefreshContentHost
{
    object? PageRefreshContent { get; }
}
