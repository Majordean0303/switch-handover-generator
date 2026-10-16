using System.IO;
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
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SwitchHandover",
        "app.log"
    );

    public static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Silently ignore logging failures
        }
    }

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

    public App()
    {
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", false);

        DispatcherUnhandledException += (s, e) =>
        {
            Log($"[DISPATCHER UNHANDLED EXCEPTION] {e.Exception.Message}\n{e.Exception}");
            MessageBox.Show($"Startup Error: {e.Exception.Message}\n\nDetails in: {LogFilePath}", "Switch Handover Generator Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log($"[APPDOMAIN UNHANDLED EXCEPTION] {ex.Message}\n{ex}");
            }
        };
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            Log("Application starting up...");
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            Log("MainWindow displayed successfully.");
        }
        catch (Exception ex)
        {
            Log($"[STARTUP FATAL] {ex.Message}\n{ex}");
            MessageBox.Show($"Fatal Startup Error: {ex.Message}\n\nCheck log: {LogFilePath}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
