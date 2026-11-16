using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// Finds the reload command of the page currently shown by the shell so the single shell
/// Refresh control can reload that page in place.
/// </summary>
public static class PageRefreshResolver
{
    /// <summary>Name of the view-model property found by convention.</summary>
    public const string ConventionalCommandPropertyName = "RefreshCommand";

    private const int MaxHostDepth = 4;

    /// <summary>
    /// Resolves the refresh command for <paramref name="pageContent"/> (the navigation frame's content).
    /// Order: <see cref="IPageRefreshTarget"/> on the content or its data context, then a public
    /// <c>RefreshCommand</c> property on the data context, then on the content itself.
    /// Chrome wrappers implementing <see cref="IPageRefreshContentHost"/> and hosting
    /// <see cref="Frame"/>s are unwrapped first. A page or view model marked
    /// <see cref="IPageRefreshOptOut"/> is never matched by convention.
    /// </summary>
    public static ICommand? Resolve(object? pageContent) => Resolve(pageContent, 0);

    /// <summary>
    /// Reloads the page in place when it exposes a refresh command.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a refresh command was found, including when it is busy and
    /// declined to run again; <see langword="false"/> when the page has no refresh command and
    /// the caller should fall back to re-navigation.
    /// </returns>
    public static bool TryRefreshInPlace(object? pageContent)
    {
        var command = Resolve(pageContent);
        if (command is null)
        {
            return false;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        return true;
    }

    /// <summary>
    /// Reloads every page in <paramref name="pageContents"/> that exposes a refresh command which
    /// can execute. Content without a command, and commands that are busy, are skipped; nothing is
    /// re-navigated. Used for the pages docked in a workspace shell and for split panes.
    /// </summary>
    /// <returns>The number of refresh commands executed.</returns>
    public static int RefreshAllInPlace(IEnumerable<object?>? pageContents)
    {
        if (pageContents is null)
        {
            return 0;
        }

        var executed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var content in pageContents)
        {
            // The same view model can back more than one pane; reload it once.
            if (Resolve(content) is { } command && command.CanExecute(null) && executed.Add(command))
            {
                command.Execute(null);
            }
        }

        return executed.Count;
    }

    private static ICommand? Resolve(object? candidate, int depth)
    {
        if (candidate is null || depth > MaxHostDepth)
        {
            return null;
        }

        if (candidate is IPageRefreshContentHost host)
        {
            return Resolve(host.PageRefreshContent, depth + 1);
        }

        if (candidate is IPageRefreshTarget target)
        {
            return target.RefreshCommand;
        }

        if (candidate is Frame frame)
        {
            // Dock panes and split panes host their page inside a frame.
            return Resolve(frame.Content, depth + 1);
        }

        var dataContext = candidate is FrameworkElement element ? element.DataContext : null;
        if (ReferenceEquals(dataContext, candidate))
        {
            dataContext = null;
        }

        if (dataContext is IPageRefreshTarget dataContextTarget)
        {
            return dataContextTarget.RefreshCommand;
        }

        if (dataContext is IPageRefreshOptOut || candidate is IPageRefreshOptOut)
        {
            return null;
        }

        return ResolveByConvention(dataContext) ?? ResolveByConvention(candidate);
    }

    private static ICommand? ResolveByConvention(object? candidate)
    {
        if (candidate is null)
        {
            return null;
        }

        try
        {
            var property = candidate.GetType().GetProperty(
                ConventionalCommandPropertyName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is null ||
                !property.CanRead ||
                property.GetIndexParameters().Length != 0 ||
                !typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                return null;
            }

            return property.GetValue(candidate) as ICommand;
        }
        catch (AmbiguousMatchException)
        {
            // A derived view model hides the base property; treat the page as having no single reload command.
            return null;
        }
    }
}
