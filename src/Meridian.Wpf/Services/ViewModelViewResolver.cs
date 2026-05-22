using System;
using System.Reflection;
using System.Windows;
using Meridian.Wpf.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.Services;

public sealed class ViewModelViewResolver : IViewModelViewResolver
{
    private readonly IServiceProviderIsService? _serviceLookup;
    private readonly ILogger<ViewModelViewResolver>? _logger;
    private readonly Dictionary<Type, Type?> _viewModelTypeCache = [];

    public ViewModelViewResolver(
        IServiceProviderIsService? serviceLookup = null,
        ILogger<ViewModelViewResolver>? logger = null)
    {
        _serviceLookup = serviceLookup;
        _logger = logger;
    }

    public Type? ResolveViewModelType(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (_viewModelTypeCache.TryGetValue(pageType, out var cachedType))
        {
            return cachedType;
        }

        var resolvedType = ResolveViewModelTypeCore(pageType);
        _viewModelTypeCache[pageType] = resolvedType;
        return resolvedType;
    }

    public void AutoWire(FrameworkElement page, IServiceProvider scope)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(scope);

        if (page.DataContext is not null)
        {
            return;
        }

        var viewModelType = ResolveViewModelType(page.GetType());
        if (viewModelType is null)
        {
            return;
        }

        if (scope.GetService(viewModelType) is { } viewModel)
        {
            page.DataContext = viewModel;
        }
    }

    public void LogMissingViewModels(IShellPageRegistry pageRegistry)
    {
        ArgumentNullException.ThrowIfNull(pageRegistry);

        foreach (var page in pageRegistry.Pages)
        {
            var viewModelType = ResolveViewModelType(page.PageType);
            if (viewModelType is null)
            {
                _logger?.LogWarning(
                    "No matching ViewModel type was found for shell page {PageTag} ({PageType}). Expected convention: {Convention}.",
                    page.PageTag,
                    page.PageType.Name,
                    "{PageName}ViewModel");
                continue;
            }

            if (_serviceLookup is not null && !_serviceLookup.IsService(viewModelType))
            {
                _logger?.LogWarning(
                    "Matching ViewModel type {ViewModelType} for shell page {PageTag} ({PageType}) is not registered in the DI container.",
                    viewModelType.Name,
                    page.PageTag,
                    page.PageType.Name);
            }
        }
    }

    private static Type? ResolveViewModelTypeCore(Type pageType)
    {
        var pageName = pageType.Name;
        var names = GetCandidateNames(pageName).ToArray();

        return GetLoadableTypes(pageType.Assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false } && names.Contains(type.Name, StringComparer.Ordinal))
            .OrderBy(type => GetNamespaceRank(pageType, type))
            .ThenBy(type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetCandidateNames(string pageName)
    {
        if (pageName.EndsWith("Page", StringComparison.Ordinal) && pageName.Length > "Page".Length)
        {
            yield return pageName[..^"Page".Length] + "ViewModel";
        }

        yield return pageName + "ViewModel";
    }

    private static int GetNamespaceRank(Type pageType, Type viewModelType)
    {
        if (string.Equals(pageType.Namespace, viewModelType.Namespace, StringComparison.Ordinal))
        {
            return 0;
        }

        if (string.Equals(viewModelType.Namespace, "Meridian.Wpf.ViewModels", StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
