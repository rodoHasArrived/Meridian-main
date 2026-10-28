using System;
using System.Windows;

namespace Meridian.Wpf.Services;

/// <summary>
/// Resolves a page's view-model type and wires its data context from a service scope.
/// </summary>
public interface IViewModelViewResolver
{
    Type? ResolveViewModelType(Type pageType);

    void AutoWire(FrameworkElement page, IServiceProvider scope);
}
