using System.Reflection;
using System.Windows;
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
    /// Chrome wrappers implementing <see cref="IPageRefreshContentHost"/> are unwrapped first.
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

        var dataContext = candidate is FrameworkElement element ? element.DataContext : null;
        if (ReferenceEquals(dataContext, candidate))
        {
            dataContext = null;
        }

        if (dataContext is IPageRefreshTarget dataContextTarget)
        {
            return dataContextTarget.RefreshCommand;
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
