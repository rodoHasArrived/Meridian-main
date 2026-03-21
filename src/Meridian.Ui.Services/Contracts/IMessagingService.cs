namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for in-process pub/sub messaging between UI components.
/// Shared between WPF desktop applications.
/// </summary>
public interface IMessagingService
{
    void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
    void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
    void Publish<TMessage>(TMessage message) where TMessage : class;
}
