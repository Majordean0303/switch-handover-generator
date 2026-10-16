using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwitchHandover.App.ViewModels;
using SwitchHandover.App.Views;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace SwitchHandover.App;

public partial class App : Application
{
    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();

            // Windows
            services.AddSingleton<MainWindow>();
        }).Build();

    public static T GetService<T>() where T : class
    {
        return (_host.Services.GetService(typeof(T)) as T)!;
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
