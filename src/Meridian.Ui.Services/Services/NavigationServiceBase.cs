using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Abstract base class for navigation management shared across desktop applications.
/// Provides shared page registry, history tracking, breadcrumb generation, and event raising.
/// Platform-specific frame navigation is delegated to derived classes.
/// Part of Phase 6C.2 service deduplication (ROADMAP item 6C.2).
/// </summary>
public abstract class NavigationServiceBase
{
    private readonly Stack<NavigationEntry> _navigationHistory = new();
    private readonly Dictionary<string, Type> _pageRegistry = new();

    /// <summary>Event raised when navigation occurs.</summary>
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>Gets whether navigation can go back in the history.</summary>
    public virtual bool CanGoBack => _navigationHistory.Count > 1;

    /// <summary>
    /// Initializes the navigation service. Call RegisterPages() to populate the page registry.
    /// </summary>
    protected NavigationServiceBase()
    {
        RegisterAllPages();
    }

    /// <summary>
    /// Navigates to a page by tag name.
    /// </summary>
    /// <param name="pageTag">The page tag to navigate to.</param>
    /// <param name="parameter">Optional navigation parameter.</param>
    /// <returns>True if navigation succeeded.</returns>
    public bool NavigateTo(string pageTag, object? parameter = null)
    {
        if (!_pageRegistry.TryGetValue(pageTag, out var pageType))
        {
            OnNavigationFailed(pageTag);
            return false;
        }

        var result = NavigateToPageCore(pageType, parameter);

        if (result)
        {
            var entry = new NavigationEntry
            {
                PageTag = pageTag,
                Parameter = parameter,
                Timestamp = DateTime.UtcNow
            };
            _navigationHistory.Push(entry);

            Navigated?.Invoke(this, new NavigationEventArgs
            {
                PageTag = pageTag,
                Parameter = parameter
            });
        }

        return result;
    }

    /// <summary>
    /// Navigates to a page type directly.
    /// </summary>
    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        return NavigateToPageCore(pageType, parameter);
    }

    /// <summary>
    /// Navigates back in the history.
    /// </summary>
    public virtual void GoBack()
    {
        if (CanGoBack)
        {
            GoBackCore();
            if (_navigationHistory.Count > 0)
            {
                _navigationHistory.Pop();
            }
        }
    }

    /// <summary>
    /// Gets the page type for a given tag.
    /// </summary>
    public Type? GetPageType(string pageTag)
    {
        return _pageRegistry.TryGetValue(pageTag, out var pageType) ? pageType : null;
    }

    /// <summary>
    /// Gets navigation breadcrumbs (history entries).
    /// </summary>
    public IReadOnlyList<NavigationEntry> GetBreadcrumbs()
    {
        return _navigationHistory.ToArray();
    }

    /// <summary>
    /// Gets all registered page tags.
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredPages() => _pageRegistry.Keys;

    /// <summary>
    /// Checks if a page tag is registered.
    /// </summary>
    public bool IsPageRegistered(string pageTag) => _pageRegistry.ContainsKey(pageTag);

    /// <summary>
    /// Gets the current page tag from history.
    /// </summary>
    public string? GetCurrentPageTag()
    {
        return _navigationHistory.Count > 0
            ? _navigationHistory.Peek().PageTag
            : null;
    }

    /// <summary>
    /// Clears navigation history.
    /// </summary>
    public virtual void ClearHistory()
    {
        _navigationHistory.Clear();
        ClearHistoryCore();
    }

    /// <summary>
    /// Registers a page in the page registry.
    /// </summary>
    /// <param name="tag">The unique tag for the page.</param>
    /// <param name="pageType">The Type of the page.</param>
    protected void RegisterPage(string tag, Type pageType)
    {
        _pageRegistry[tag] = pageType;
    }

    /// <summary>
    /// When overridden, registers all navigable pages by calling RegisterPage for each.
    /// </summary>
    protected abstract void RegisterAllPages();

    /// <summary>
    /// When overridden, performs the platform-specific navigation to a page type.
    /// </summary>
    /// <param name="pageType">The Type of the page to navigate to.</param>
    /// <param name="parameter">Optional navigation parameter.</param>
    /// <returns>True if navigation succeeded.</returns>
    protected abstract bool NavigateToPageCore(Type pageType, object? parameter);

    /// <summary>
    /// When overridden, performs the platform-specific back navigation.
    /// </summary>
    protected abstract void GoBackCore();

    /// <summary>
    /// When overridden, clears the platform-specific navigation stack.
    /// </summary>
    protected abstract void ClearHistoryCore();

    /// <summary>
    /// Called when navigation to an unknown page tag is attempted.
    /// </summary>
    protected virtual void OnNavigationFailed(string pageTag)
    {
    }
}
