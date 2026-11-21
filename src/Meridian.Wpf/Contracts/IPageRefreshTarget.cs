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

/// <summary>
/// Marks a page or view model whose public <c>RefreshCommand</c> does more than re-read data
/// (for example it loads plugins, writes, sends, or starts jobs). The shell refresh does not pick
/// such a command up by convention and falls back to re-navigating the page instead. A type that
/// also implements <see cref="IPageRefreshTarget"/> has opted back in explicitly.
/// </summary>
public interface IPageRefreshOptOut
{
}
