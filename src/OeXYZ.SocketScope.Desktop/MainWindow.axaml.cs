using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.ViewModels;
using OeXYZ.SocketScope.Desktop.Views;

namespace OeXYZ.SocketScope.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel? viewModel;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AttachResponsiveBehavior();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        UserFileDialogService fileDialogs,
        DesktopClipboardService clipboard)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(fileDialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        AvaloniaXamlLoader.Load(this);
        AttachResponsiveBehavior();
        DataContext = viewModel;
        fileDialogs.Attach(this);
        clipboard.Attach(this);
        Opened += OnOpened;
        viewModel.RestartRequested += OnRestartRequested;
    }

    private void AttachResponsiveBehavior()
    {
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) => UpdateResponsiveLayout(ClientSize.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        _ = sender;
        UpdateResponsiveLayout(eventArgs.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid shell = this.FindControl<Grid>("ShellGrid")
            ?? throw new InvalidOperationException("The application shell is missing.");
        Border sidebar = this.FindControl<Border>("Sidebar")
            ?? throw new InvalidOperationException("The sidebar is missing.");
        Border compactNavigation = this.FindControl<Border>("CompactNavigation")
            ?? throw new InvalidOperationException("The compact navigation is missing.");
        TextBlock lastUpdatedLabel = this.FindControl<TextBlock>("LastUpdatedLabel")
            ?? throw new InvalidOperationException("The update label is missing.");
        TextBlock lastUpdatedValue = this.FindControl<TextBlock>("LastUpdatedValue")
            ?? throw new InvalidOperationException("The update value is missing.");

        bool compact = width < 980;
        shell.ColumnDefinitions[0].Width = compact
            ? new GridLength(0)
            : new GridLength(width >= 1600 ? 240 : 220);
        sidebar.IsVisible = !compact;
        compactNavigation.IsVisible = compact;

        bool showUpdateTime = width >= 760;
        lastUpdatedLabel.IsVisible = showUpdateTime;
        lastUpdatedValue.IsVisible = showUpdateTime;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (viewModel is not null)
        {
            _ = viewModel.InitializeAsync();
        }
    }

    private void OnRestartRequested(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (viewModel is not null && e.Key == Key.F1)
        {
            viewModel.NavigateCommand.Execute(nameof(ApplicationSection.Help));
            e.Handled = true;
            return;
        }

        if (viewModel is not null
            && e.Key == Key.F5
            && e.KeyModifiers == KeyModifiers.None
            && viewModel.RefreshCommand.CanExecute(null))
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (viewModel is not null
            && e.Key == Key.F
            && e.KeyModifiers == KeyModifiers.Control)
        {
            viewModel.NavigateCommand.Execute(nameof(ApplicationSection.Connections));
            Dispatcher.UIThread.Post(() =>
            {
                this.FindControl<InventoryView>("InventoryContent")?.FocusSearch();
            }, DispatcherPriority.Input);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
