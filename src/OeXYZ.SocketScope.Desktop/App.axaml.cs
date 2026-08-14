using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;
using OeXYZ.SocketScope.Desktop.ViewModels;

namespace OeXYZ.SocketScope.Desktop;

public sealed partial class App : Application
{
    private ServiceProvider? services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceCollection registrations = new();
            var dataPaths = ApplicationDataPaths.Resolve();
            registrations.AddSingleton(dataPaths);
            registrations.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.Services.AddSingleton<ILoggerProvider, PrivacySafeJsonLoggerProvider>();
            });
            registrations.AddSingleton<UiStrings>();
            registrations.AddSingleton<ApplicationStorage>();
            registrations.AddSingleton<UserFileDialogService>();
            registrations.AddSingleton<IUserFileDialogService>(provider => provider.GetRequiredService<UserFileDialogService>());
            registrations.AddSingleton<DesktopClipboardService>();
            registrations.AddSingleton<IDesktopClipboardService>(provider => provider.GetRequiredService<DesktopClipboardService>());
            registrations.AddSingleton<IDesktopUpdateService, DesktopUpdateService>();
            registrations.AddSingleton<IDesktopTerminalIntegrationService, DesktopTerminalIntegrationService>();
            registrations.AddSingleton(provider => DesktopComposition.CreateCoordinator(
                provider.GetRequiredService<ILogger<InventoryCoordinator>>()));
            registrations.AddSingleton<MainWindowViewModel>();
            registrations.AddSingleton<MainWindow>();
            services = registrations.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            desktop.MainWindow = services.GetRequiredService<MainWindow>();
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        services?.Dispose();
        services = null;
    }
}
