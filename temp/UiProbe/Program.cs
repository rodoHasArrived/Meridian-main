using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            var dictionaries = Application.Current!.Resources.MergedDictionaries;
            foreach (var uri in new[]
            {
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml",
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTokens.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/AppStyles.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeTypography.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeSurfaces.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/ThemeControls.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/IconResources.xaml",
                "pack://application:,,,/Meridian.Desktop;component/Styles/Animations.xaml"
            })
            {
                dictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
            }

            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find App.ConfigureServices.");

            configureServices.Invoke(null, [services, configuration]);

            var provider = services.BuildServiceProvider();
            var page = provider.GetRequiredService<Meridian.Wpf.Views.DashboardPage>();

            var frame = new System.Windows.Controls.Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };

            frame.Navigate(page);
            frame.ApplyTemplate();
            frame.UpdateLayout();
            Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Console.WriteLine("DashboardPage navigated successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            for (var current = ex; current is not null; current = current.InnerException)
            {
                Console.WriteLine($"EXCEPTION: {current.GetType().FullName}");
                Console.WriteLine($"MESSAGE: {current.Message}");
                Console.WriteLine("---");
            }

            return 1;
        }
    }
}
