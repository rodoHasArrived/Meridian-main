using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Base class for all WPF ViewModels.
/// Extends <see cref="ObservableObject"/> so that ViewModels marked <c>partial</c>
/// can use the <c>[ObservableProperty]</c> and <c>[RelayCommand]</c> source generators
/// from CommunityToolkit.Mvvm without any structural changes to existing call sites.
/// </summary>
public abstract class BindableBase : ObservableObject
{
    /// <summary>
    /// Raises <see cref="ObservableObject.PropertyChanged"/> for the specified property.
    /// Kept for backwards compatibility with code that calls <c>RaisePropertyChanged(nameof(X))</c>.
    /// </summary>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => OnPropertyChanged(propertyName);
}
