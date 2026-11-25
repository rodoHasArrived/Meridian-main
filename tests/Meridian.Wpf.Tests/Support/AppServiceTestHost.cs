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
            return;
        }

        var configuration = new ConfigurationBuilder().Build();
        configureServices.Invoke(null, [services, configuration]);
    }
}
