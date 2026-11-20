using Meridian.Contracts.AssetOperations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

public static class BookReceiptServiceCollectionExtensions
{
    public static IServiceCollection AddBookReceiptQueueServices(
        this IServiceCollection services, Func<IServiceProvider, string> dataDirectory)
    {
        services.TryAddSingleton<IBookReceiptEvidenceSource, BookReceiptEvidenceSource>();
        services.TryAddSingleton<IBookReceiptSource, BookReceiptSource>();
        services.TryAddSingleton<IBookReceiptRunStore>(sp => new FileBookReceiptRunStore(dataDirectory(sp)));
        services.TryAddSingleton<IBookReceiptQueueService, BookReceiptQueueService>();
        return services;
    }
}
