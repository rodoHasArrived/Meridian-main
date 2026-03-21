using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Wpf.Contracts;

/// <summary>
/// Interface for managing navigation throughout the application.
/// Enables testability and dependency injection.
/// Phase 6C.2: Navigation types (NavigationEntry, NavigationEventArgs) are now
/// shared from Meridian.Ui.Services.Contracts.
/// </summary>
public interface INavigationService
{
    /// <summary>Gets whether the navigation service has been initialized with a frame.</summary>
    bool IsInitialized { get; }

    /// <summary>Gets whether navigation can go back.</summary>
    bool CanGoBack { get; }

    /// <summary>Initializes the navigation service with the main frame.</summary>
    void Initialize(Frame frame);

    /// <summary>Navigates to a page by tag name.</summary>
    bool NavigateTo(string pageTag, object? parameter = null);

    /// <summary>Navigates to a page type directly.</summary>
    bool NavigateTo(Type pageType, object? parameter = null);

    /// <summary>Navigates back.</summary>
    void GoBack();

    /// <summary>Gets the page type for a given tag.</summary>
    Type? GetPageType(string pageTag);

    /// <summary>Gets navigation breadcrumbs.</summary>
    IReadOnlyList<NavigationEntry> GetBreadcrumbs();

    /// <summary>Gets all registered page tags.</summary>
    IReadOnlyCollection<string> GetRegisteredPages();

    /// <summary>Checks if a page tag is registered.</summary>
    bool IsPageRegistered(string pageTag);

    /// <summary>Event raised when navigation occurs.</summary>
    event EventHandler<NavigationEventArgs>? Navigated;
}
