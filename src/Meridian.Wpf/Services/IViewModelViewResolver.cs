using System;
using System.Windows;

namespace Meridian.Wpf.Services;

public interface IViewModelViewResolver
{
    Type? ResolveViewModelType(Type pageType);

    void AutoWire(FrameworkElement page, IServiceProvider scope);
}
