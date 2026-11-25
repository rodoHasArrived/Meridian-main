using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Support;

public static class AppServiceTestHost
{
    public static void InvokeConfigureServices(MethodInfo configureServices, IServiceCollection services)
    {
        if (configureServices.GetParameters().Length == 1)
        {
            configureServices.Invoke(null, [services]);
        }
        else
        {
            var configuration = new ConfigurationBuilder().Build();
            configureServices.Invoke(null, [services, configuration]);
        }

        // Outermost on every named client, so a request to the absent local backend is refused
        // before the retry policy can wait on it. See LoopbackFastRefusalHandler.
        services.ConfigureHttpClientDefaults(static builder =>
            builder.AddHttpMessageHandler(static () => new LoopbackFastRefusalHandler()));
    }
}
