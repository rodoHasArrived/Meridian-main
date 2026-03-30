using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Interface for ViewModels that provide contextual commands to the command palette.
/// Implement this on ViewModels to register context-specific commands when activated,
/// and unregister them when deactivated.
/// </summary>
public interface ICommandContextProvider
{
    /// <summary>
    /// Gets the unique context key identifying this provider.
    /// </summary>
    string ContextKey { get; }

    /// <summary>
    /// Gets the list of contextual commands available in this context.
    /// </summary>
    IReadOnlyList<CommandEntry> GetContextualCommands();

    /// <summary>
    /// Called when the page/ViewModel becomes active.
    /// Register the contextual provider with the command palette service here.
    /// </summary>
    void OnActivated();

    /// <summary>
    /// Called when the page/ViewModel is navigated away from.
    /// Unregister the contextual provider with the command palette service here.
    /// </summary>
    void OnDeactivated();
}
