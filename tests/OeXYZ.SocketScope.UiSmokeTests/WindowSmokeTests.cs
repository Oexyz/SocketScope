using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Export;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Desktop;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;
using OeXYZ.SocketScope.Desktop.ViewModels;
using OeXYZ.SocketScope.Desktop.Views;
using OeXYZ.SocketScope.Updater;

namespace OeXYZ.SocketScope.UiSmokeTests;

public sealed class WindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_LoadsEveryProductionViewFromLocalResources()
    {
        MainWindow window = new();
        window.Show();

        Assert.Single(window.GetLogicalDescendants().OfType<OverviewView>());
        Assert.Single(window.GetLogicalDescendants().OfType<InventoryView>());
        Assert.Single(window.GetLogicalDescendants().OfType<ChangesView>());
        Assert.Single(window.GetLogicalDescendants().OfType<ReportsView>());
        Assert.Single(window.GetLogicalDescendants().OfType<SettingsView>());
        Assert.Single(window.GetLogicalDescendants().OfType<HelpView>());
        Assert.Equal(760, window.MinWidth);
        Assert.Equal(520, window.MinHeight);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_PrimaryNavigationCanReceiveKeyboardFocus()
    {
        MainWindow window = new();
        window.Show();
        Button navigation = window.FindControl<Button>("OverviewNavigation")
            ?? throw new InvalidOperationException("Overview navigation was not created.");

        bool focused = navigation.Focus();

        Assert.True(focused);
        Assert.True(navigation.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void DataTables_ExposeLocalizedMovableResizableHeadersAndFullProcessIdentifiers()
    {
        InventoryView inventory = new();
        OverviewView overview = new();
        ChangesView changes = new();
        Window window = new()
        {
            Content = new StackPanel { Children = { inventory, overview, changes } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        DataGrid inventoryGrid = inventory.FindControl<DataGrid>("InventoryDataGrid")!;
        DataGrid overviewGrid = overview.FindControl<DataGrid>("OverviewDataGrid")!;
        DataGrid changesGrid = changes.FindControl<DataGrid>("ChangesDataGrid")!;
        Assert.Equal(105, inventoryGrid.Columns[0].Width.Value);
        Assert.Equal(105, overviewGrid.Columns[0].Width.Value);
        Assert.Equal(105, changesGrid.Columns[2].Width.Value);
        Assert.Equal(105, inventoryGrid.Columns[5].Width.Value);
        Assert.Equal(105, overviewGrid.Columns[5].Width.Value);
        Assert.All([inventoryGrid, overviewGrid, changesGrid], grid =>
        {
            Assert.Equal(DataGridHeadersVisibility.Column, grid.HeadersVisibility);
            Assert.True(grid.CanUserReorderColumns);
            Assert.True(grid.CanUserResizeColumns);
            Assert.False(grid.CanUserSortColumns);
            Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
            Assert.Equal(DataGridClipboardCopyMode.IncludeHeader, grid.ClipboardCopyMode);
            Assert.All(
                grid.Columns.OfType<DataGridTemplateColumn>(),
                column => Assert.NotNull(column.ClipboardContentBinding));
        });

        inventoryGrid.Columns[6].DisplayIndex = 0;
        inventoryGrid.Columns[1].Width = new DataGridLength(220);

        Assert.Equal(0, inventoryGrid.Columns[6].DisplayIndex);
        Assert.Equal(220, inventoryGrid.Columns[1].Width.Value);

        inventory.FindControl<Button>("ResetInventoryColumnsButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overview.FindControl<Button>("ResetOverviewColumnsButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        changes.FindControl<Button>("ResetChangesColumnsButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(0, inventoryGrid.Columns[0].DisplayIndex);
        Assert.Equal(6, inventoryGrid.Columns[6].DisplayIndex);
        Assert.Equal(175, inventoryGrid.Columns[1].Width.Value);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DataTable_CtrlC_CopiesSelectedRowsWithLocalizedHeaders()
    {
        UiStrings strings = new();
        ObservationRowViewModel row = new(CreateSensitiveObservation(), strings);
        OverviewView overview = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 1100,
            Height = 650,
            Content = overview,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        DataGrid grid = overview.FindControl<DataGrid>("OverviewDataGrid")!;
        grid.ItemsSource = new[] { row };
        grid.SelectedIndex = 0;
        Assert.True(grid.Focus());

        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        Dispatcher.UIThread.RunJobs();
        IClipboard clipboard = window.Clipboard
            ?? throw new InvalidOperationException("The headless clipboard is unavailable.");
        string? copied = await clipboard.TryGetTextAsync();

        Assert.NotNull(copied);
        Assert.Contains("Protocol", copied, StringComparison.Ordinal);
        Assert.Contains("TCP4", copied, StringComparison.Ordinal);
        Assert.Contains("192.168.50.9:4567", copied, StringComparison.Ordinal);
        window.Close();
    }

    [Fact]
    public void ObservationClipboardFormatter_AppliesShareSafeProfileWithoutChangingDiagnosticFacts()
    {
        UiStrings strings = new();
        SocketObservation observation = CreateSensitiveObservation();
        InventoryGeneration generation = new(
            Guid.Parse("602c0093-c455-46a4-829e-c0bb3834cb47"),
            observation.FirstObservedAt,
            observation.FirstObservedAt.AddMilliseconds(25),
            "test-namespace",
            [observation],
            [],
            true);

        string full = ObservationClipboardFormatter.Create(
            generation,
            observation,
            "1.0.0",
            ReportProfile.Full,
            new ReportRedactionOptions(),
            ["secret-process"],
            strings);
        string shareSafe = ObservationClipboardFormatter.Create(
            generation,
            observation,
            "1.0.0",
            ReportProfile.ShareSafe,
            new ReportRedactionOptions(),
            ["secret-process"],
            strings);

        Assert.Contains("192.168.50.9", full, StringComparison.Ordinal);
        Assert.Contains(@"C:\Users\ExampleUser\private\secret.exe", full, StringComparison.Ordinal);
        Assert.Contains("secret-process", full, StringComparison.Ordinal);
        Assert.Contains("container-secret", full, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.50.9", shareSafe, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.10", shareSafe, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\ExampleUser\private\secret.exe", shareSafe, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-process", shareSafe, StringComparison.Ordinal);
        Assert.DoesNotContain("container-secret", shareSafe, StringComparison.Ordinal);
        Assert.Contains("4567", shareSafe, StringComparison.Ordinal);
        Assert.Contains(strings.NotTested, shareSafe, StringComparison.Ordinal);
        Assert.Contains(strings.ShareSafeClipboardNotice, shareSafe, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task EndpointAndDiagnosticCopyCommands_UseTheSelectedEvidenceAndExcludeDiagnosticDetail()
    {
        string? previousDataRoot = Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT");
        string root = Path.Combine(AppContext.BaseDirectory, "clipboard-ui-test", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", root);
        try
        {
            ControlledProviders providers = new();
            InventoryCoordinator coordinator = new(
                providers,
                providers,
                providers,
                providers,
                providers,
                providers,
                TimeProvider.System,
                NullLogger<InventoryCoordinator>.Instance);
            using RecordingClipboardService clipboard = new();
            using ApplicationStorage storage = new();
            using MainWindowViewModel viewModel = new(
                coordinator,
                storage,
                new NullFileDialogService(),
                clipboard,
                new NullUpdateService(),
                new NullTerminalIntegrationService(),
                new UiStrings());

            await viewModel.InitializeAsync();
            viewModel.SelectedObservation = Assert.Single(viewModel.OverviewRows);
            Assert.True(viewModel.CopyLocalEndpointCommand.CanExecute(null));
            Assert.False(viewModel.CopyRemoteEndpointCommand.CanExecute(null));
            Assert.True(viewModel.CopyExecutablePathCommand.CanExecute(null));

            viewModel.CopyLocalEndpointCommand.Execute(null);
            string local = await clipboard.WaitForNextAsync();
            Assert.Equal("192.168.50.9:4567", local);

            viewModel.CopyDetailsCommand.Execute(null);
            string full = await clipboard.WaitForNextAsync();
            Assert.Contains("secret-process", full, StringComparison.Ordinal);
            Assert.Contains(@"C:\Users\ExampleUser\private\secret.exe", full, StringComparison.Ordinal);

            viewModel.ReduceProcessNames = true;
            viewModel.CopyShareSafeSummaryCommand.Execute(null);
            string shareSafe = await clipboard.WaitForNextAsync();
            Assert.DoesNotContain("192.168.50.9", shareSafe, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-process", shareSafe, StringComparison.Ordinal);
            Assert.DoesNotContain("container-secret", shareSafe, StringComparison.Ordinal);

            viewModel.CopyDiagnosticsCommand.Execute(null);
            string diagnostics = await clipboard.WaitForNextAsync();
            Assert.Contains("controlled.partial", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("diagnostic-detail-secret", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("192.168.50.9", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-process", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\ExampleUser", diagnostics, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", previousDataRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void HelpView_ReflowsGlossaryWithoutClippingItsCardOrder()
    {
        HelpView help = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 680,
            Height = 700,
            Content = help,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid glossary = help.FindControl<Grid>("GlossaryGrid")!;
        Border unknown = help.FindControl<Border>("UnknownCard")!;
        Assert.Single(glossary.ColumnDefinitions);
        Assert.Equal(4, Grid.GetRow(unknown));
        Assert.Equal(1, Grid.GetColumnSpan(unknown));

        window.Width = 1000;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, glossary.ColumnDefinitions.Count);
        Assert.Equal(2, Grid.GetRow(unknown));
        Assert.Equal(2, Grid.GetColumnSpan(unknown));
        window.Close();
    }

    [AvaloniaFact]
    public void InventoryTable_ColumnHeaderCanBeReorderedWithMouseDrag()
    {
        InventoryView inventory = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 1200,
            Height = 700,
            Content = inventory,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        DataGrid grid = inventory.FindControl<DataGrid>("InventoryDataGrid")!;
        DataGridColumnHeader[] headers = [.. grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .Where(header => header.Content is string)];
        DataGridColumnHeader protocol = headers.Single(header => Equals(header.Content, "Protocol"));
        DataGridColumnHeader process = headers.Single(header => Equals(header.Content, "Process"));
        Point protocolOrigin = protocol.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("Protocol header is outside the test window.");
        Point processOrigin = process.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("Process header is outside the test window.");
        Point start = protocolOrigin + new Vector(protocol.Bounds.Width / 2, protocol.Bounds.Height / 2);
        Point intermediate = start + new Vector(20, 0);
        Point target = processOrigin + new Vector(process.Bounds.Width * 0.75, process.Bounds.Height / 2);

        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(intermediate, RawInputModifiers.LeftMouseButton);
        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        window.MouseUp(target, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, grid.Columns[0].DisplayIndex);
        Assert.Equal(0, grid.Columns[1].DisplayIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_FilterAndLanguageSelectorsHaveVisibleDefaults()
    {
        string? previousDataRoot = Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT");
        Environment.SetEnvironmentVariable(
            "SOCKETSCOPE_DATA_ROOT",
            Path.Combine(AppContext.BaseDirectory, "selector-test-data"));
        try
        {
            EmptyProviders providers = new();
            InventoryCoordinator coordinator = new(
                providers,
                providers,
                providers,
                providers,
                providers,
                providers,
                TimeProvider.System,
                NullLogger<InventoryCoordinator>.Instance);
            using ApplicationStorage storage = new();
            UiStrings strings = new();
            using MainWindowViewModel viewModel = new(
                coordinator,
                storage,
                new NullFileDialogService(),
                new NullClipboardService(),
                new NullUpdateService(),
                new NullTerminalIntegrationService(),
                strings);
            MainWindow window = new(viewModel, new UserFileDialogService(), new DesktopClipboardService());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            InventoryView inventory = window.GetLogicalDescendants().OfType<InventoryView>().Single();
            SettingsView settings = window.GetLogicalDescendants().OfType<SettingsView>().Single();
            ComboBox[] selectors =
            [
                inventory.FindControl<ComboBox>("ProtocolFilter")!,
                inventory.FindControl<ComboBox>("StateFilter")!,
                inventory.FindControl<ComboBox>("ScopeFilter")!,
                inventory.FindControl<ComboBox>("SortFilter")!,
                settings.FindControl<ComboBox>("LanguageSelector")!,
                settings.FindControl<ComboBox>("ExportProfileSelector")!,
            ];

            Assert.All(selectors, selector =>
            {
                Assert.Equal(0, selector.SelectedIndex);
                Assert.IsType<FilterOption>(selector.SelectedItem);
                Assert.False(string.IsNullOrWhiteSpace(((FilterOption)selector.SelectedItem!).Label));
            });
            Assert.Equal("en", ((FilterOption)selectors[^2].SelectedItem!).Value);
            Assert.Equal("English", ((FilterOption)selectors[^2].SelectedItem!).Label);
            Assert.Equal(nameof(OeXYZ.SocketScope.Core.Export.ReportProfile.ShareSafe), ((FilterOption)selectors[^1].SelectedItem!).Value);

            TextBox customSensitiveValues = settings.FindControl<TextBox>("CustomSensitiveValuesText")!;
            Assert.Equal(string.Empty, customSensitiveValues.Text);
            Assert.True(viewModel.CustomSensitiveValuesAreValid);
            customSensitiveValues.Text = "ab";
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.CustomSensitiveValuesAreValid);
            Assert.DoesNotContain("ab", viewModel.CustomSensitiveValuesStatus, StringComparison.OrdinalIgnoreCase);
            customSensitiveValues.Text = "Customer Alpha";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CustomSensitiveValuesAreValid);
            Assert.DoesNotContain("Customer Alpha", viewModel.CustomSensitiveValuesStatus, StringComparison.OrdinalIgnoreCase);

            DataGrid inventoryGrid = inventory.FindControl<DataGrid>("InventoryDataGrid")!;
            Assert.Equal("Protocol", inventoryGrid.Columns[0].Header);
            Assert.Equal("Local", inventoryGrid.Columns[1].Header);

            window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsHelp);
            Assert.True(window.GetLogicalDescendants().OfType<HelpView>().Single().IsVisible);

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsConnections);
            Assert.True(inventory.FindControl<TextBox>("InventorySearch")!.IsFocused);

            strings.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Protokoll", inventoryGrid.Columns[0].Header);
            Assert.Equal("Lokal", inventoryGrid.Columns[1].Header);
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", previousDataRoot);
        }
    }

    [AvaloniaFact]
    public void MainWindow_SwitchesBetweenCompactAndSidebarNavigation()
    {
        MainWindow window = new()
        {
            WindowState = WindowState.Normal,
            Width = 800,
            Height = 600,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid shell = window.FindControl<Grid>("ShellGrid")!;
        Border sidebar = window.FindControl<Border>("Sidebar")!;
        Border compact = window.FindControl<Border>("CompactNavigation")!;
        Assert.Equal(0, shell.ColumnDefinitions[0].Width.Value);
        Assert.False(sidebar.IsVisible);
        Assert.True(compact.IsVisible);

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(220, shell.ColumnDefinitions[0].Width.Value);
        Assert.True(sidebar.IsVisible);
        Assert.False(compact.IsVisible);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1.0, 1200, 760)]
    [InlineData(1.5, 1000, 650)]
    [InlineData(2.0, 860, 600)]
    [InlineData(3.0, 760, 520)]
    public void MainWindow_ReflowsAndRendersAcrossSupportedDisplayScaling(
        double renderScaling,
        double logicalWidth,
        double logicalHeight)
    {
        MainWindow window = new()
        {
            WindowState = WindowState.Normal,
            Width = logicalWidth,
            Height = logicalHeight,
        };
        window.Show();
        window.SetRenderScaling(renderScaling);
        Dispatcher.UIThread.RunJobs();

        Grid shell = window.FindControl<Grid>("ShellGrid")!;
        Border sidebar = window.FindControl<Border>("Sidebar")!;
        Border compact = window.FindControl<Border>("CompactNavigation")!;
        OverviewView overview = window.GetLogicalDescendants().OfType<OverviewView>().Single();
        Grid overviewRoot = overview.FindControl<Grid>("LayoutRoot")!;

        Assert.Equal(renderScaling, window.RenderScaling);
        Assert.InRange(window.ClientSize.Width, logicalWidth - 1, logicalWidth + 1);
        Assert.InRange(window.ClientSize.Height, logicalHeight - 1, logicalHeight + 1);
        Assert.InRange(shell.Bounds.Width, 1, window.ClientSize.Width + 0.1);
        Assert.InRange(overviewRoot.Bounds.Width, 1, overview.Bounds.Width + 0.1);
        Assert.Equal(logicalWidth < 980, compact.IsVisible);
        Assert.Equal(logicalWidth >= 980, sidebar.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_TabKeyMovesFocusBetweenVisibleControls()
    {
        MainWindow window = new()
        {
            WindowState = WindowState.Normal,
            Width = 1200,
            Height = 760,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Button overview = window.FindControl<Button>("OverviewNavigation")!;
        Assert.True(overview.Focus());
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(overview.IsFocused);
        Assert.NotNull(window.FocusManager?.GetFocusedElement());
        window.Close();
    }

    [AvaloniaFact]
    public void ReportsView_StacksExportChoicesAtNarrowWidth()
    {
        ReportsView view = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 720,
            Height = 650,
            Content = view,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid choices = view.FindControl<Grid>("ReportChoicesGrid")!;
        Border shareSafe = view.FindControl<Border>("ShareSafeReportCard")!;
        Assert.Single(choices.ColumnDefinitions);
        Assert.Equal(1, Grid.GetRow(shareSafe));

        window.Width = 1000;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, choices.ColumnDefinitions.Count);
        Assert.Equal(1, Grid.GetColumn(shareSafe));
        Assert.Equal(0, Grid.GetRow(shareSafe));
        window.Close();
    }

    [AvaloniaFact]
    public void SettingsView_ExposesAndReflowsTerminalIntegration()
    {
        SettingsView view = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 650,
            Height = 760,
            Content = view,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid layout = view.FindControl<Grid>("TerminalStatusLayout")!;
        StackPanel actions = view.FindControl<StackPanel>("TerminalActionButtons")!;
        Button install = view.FindControl<Button>("InstallTerminalCommandButton")!;
        Button refresh = view.FindControl<Button>("RefreshTerminalStatusButton")!;
        TextBox path = view.FindControl<TextBox>("TerminalInstallPathText")!;
        Assert.Single(layout.ColumnDefinitions);
        Assert.Equal(1, Grid.GetRow(actions));
        Assert.Equal(0, Grid.GetColumn(actions));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, actions.HorizontalAlignment);
        Assert.True(path.IsReadOnly);
        Assert.NotNull(install);
        Assert.NotNull(refresh);

        window.Width = 1000;
        Dispatcher.UIThread.RunJobs();

        Assert.Single(layout.ColumnDefinitions);
        Assert.Equal(1, Grid.GetRow(actions));
        Assert.Equal(0, Grid.GetColumn(actions));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, actions.HorizontalAlignment);
        window.Close();
    }

    [AvaloniaFact]
    public void InputAndSettingsContent_StayTopAlignedAtNarrowAndWideSizes()
    {
        SettingsView settings = new();
        InventoryView inventory = new();
        Window window = new()
        {
            WindowState = WindowState.Normal,
            Width = 640,
            Height = 760,
            Content = new StackPanel { Children = { settings, inventory } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid settingsRoot = settings.FindControl<Grid>("LayoutRoot")!;
        Grid exportForm = settings.FindControl<Grid>("ExportProfileGrid")!;
        ComboBox exportProfile = settings.FindControl<ComboBox>("ExportProfileSelector")!;
        TextBox customSensitiveValues = settings.FindControl<TextBox>("CustomSensitiveValuesText")!;
        TextBox dataPath = settings.FindControl<TextBox>("DataRootPathText")!;
        TextBox search = inventory.FindControl<TextBox>("InventorySearch")!;

        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, settingsRoot.VerticalAlignment);
        Assert.Single(exportForm.ColumnDefinitions);
        Assert.Equal(1, Grid.GetRow(exportProfile));
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, exportProfile.VerticalAlignment);
        Assert.True(customSensitiveValues.AcceptsReturn);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, customSensitiveValues.VerticalContentAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, dataPath.VerticalContentAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, search.VerticalContentAlignment);

        window.Width = 1100;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, exportForm.ColumnDefinitions.Count);
        Assert.Equal(1, Grid.GetColumn(exportProfile));
        Assert.Equal(0, Grid.GetRow(exportProfile));
        window.Close();
    }

    [Fact]
    public void UiStrings_SwitchEveryUserFacingSectionWithoutPersistenceSideEffects()
    {
        UiStrings strings = new();
        Assert.Equal("Overview", strings.Overview);

        strings.SetLanguage("de");

        Assert.Equal("Übersicht", strings.Overview);
        Assert.Equal("Nicht geprüft", strings.NotTested);
        Assert.Equal("de", strings.Language);
    }

    [AvaloniaFact]
    public async Task TerminalInstallCommand_UsesReviewedServiceAndReportsNewTerminalRequirement()
    {
        string? previousDataRoot = Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT");
        string root = Path.Combine(AppContext.BaseDirectory, "terminal-ui-test", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", root);
        try
        {
            EmptyProviders providers = new();
            InventoryCoordinator coordinator = new(
                providers,
                providers,
                providers,
                providers,
                providers,
                providers,
                TimeProvider.System,
                NullLogger<InventoryCoordinator>.Instance);
            RecordingTerminalIntegrationService terminal = new();
            UiStrings strings = new();
            using ApplicationStorage storage = new();
            using MainWindowViewModel viewModel = new(
                coordinator,
                storage,
                new NullFileDialogService(),
                new NullClipboardService(),
                new NullUpdateService(),
                terminal,
                strings);

            viewModel.InstallTerminalCommand.Execute(null);
            await terminal.InstallObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int attempt = 0; attempt < 20 && viewModel.IsTerminalOperationInProgress; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Assert.Equal(1, terminal.InstallCalls);
            Assert.Equal(1, terminal.StatusCalls);
            Assert.Equal(strings.TerminalStatusNewTerminal, viewModel.TerminalStatus);
            Assert.False(viewModel.CanInstallTerminalCommand);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", previousDataRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PrivacySafeLogger_DoesNotPersistMessagesStateOrExceptionDetails()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "privacy-log-test",
            Guid.NewGuid().ToString("N"));
        ApplicationDataPaths paths = new(
            root,
            Path.Combine(root, "settings"),
            Path.Combine(root, "snapshots"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "updates"));
        const string secret = "sensitive-user-path-CONTROL-VALUE";
        try
        {
            using (PrivacySafeJsonLoggerProvider provider = new(paths))
            {
                ILogger logger = provider.CreateLogger("OeXYZ.SocketScope.Tests");
                logger.Log(
                    LogLevel.Warning,
                    new EventId(9001, "SafeEvent"),
                    secret,
                    new IOException(secret),
                    static (state, _) => $"Value: {state}");
            }

            string content = File.ReadAllText(Path.Combine(paths.Logs, "current.json"));
            Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
            Assert.DoesNotContain("Value:", content, StringComparison.Ordinal);
            Assert.Contains("SafeEvent", content, StringComparison.Ordinal);
            Assert.Contains("\"hasException\": true", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DesktopStartupOptions_PortableModeUsesApplicationDirectoryAndRemovesFlag()
    {
        string? previous = Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT");
        try
        {
            string[] remaining = DesktopStartupOptions.Apply(["--portable", "--trace"]);

            Assert.Equal(["--trace"], remaining);
            Assert.Equal(
                Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "SocketScopeData"),
                Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT", previous);
        }
    }

    private static SocketObservation CreateSensitiveObservation()
    {
        DateTimeOffset now = new(2026, 8, 14, 8, 30, 0, TimeSpan.Zero);
        const int processId = 4242;
        return new SocketObservation(
            "sensitive-observation-id",
            IdentityStrength.ProcessQualified,
            new RawSocketRecord(
                "native-sensitive-id",
                TransportProtocol.Tcp,
                IpVersion.IPv4,
                SocketRole.TcpConnection,
                new IpEndpoint("192.168.50.9", 4567),
                new IpEndpoint("203.0.113.10", 443),
                SocketState.Established,
                processId,
                now.AddMinutes(-1),
                "test-namespace",
                "ui-test-provider"),
            new ProcessMetadata(
                new ProcessIdentity(processId, now.AddHours(-1), "process-start-token"),
                "secret-process",
                @"C:\Users\ExampleUser\private\secret.exe",
                DataAvailability.Available),
            new ServiceAttribution(
                processId,
                [new ServiceIdentity("test.service", "Test service", "test", false)],
                DataAvailability.Available),
            new ContainerAttribution(
                processId,
                new ContainerIdentity("docker", "container-secret-id", "container-secret", "cgroup"),
                DataAvailability.Available),
            new BindingClassification(
                BindingScope.SpecificInterface,
                [InterfaceContext.LanPrivate],
                ["interface-id"],
                "binding.test-lan",
                EvidenceKind.Derived),
            new FirewallEvaluation(
                FirewallAssessment.NoMatchingRuleFound,
                [],
                DataAvailability.Partial,
                "firewall.partial"),
            now,
            ObservationChange.New);
    }

    private sealed class NullFileDialogService : IUserFileDialogService
    {
        public Task<string?> SelectReportTargetAsync(string suggestedName, bool html)
        {
            _ = suggestedName;
            _ = html;
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NullClipboardService : IDesktopClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            _ = text;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClipboardService : IDesktopClipboardService, IDisposable
    {
        private readonly SemaphoreSlim writes = new(0);
        private readonly Queue<string> values = new();

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (values)
            {
                values.Enqueue(text);
            }

            writes.Release();
            return Task.CompletedTask;
        }

        public async Task<string> WaitForNextAsync()
        {
            bool signaled = await writes.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(signaled, "A clipboard write was not observed before the timeout.");
            lock (values)
            {
                return values.Dequeue();
            }
        }

        public void Dispose()
        {
            writes.Dispose();
        }
    }

    private sealed class NullUpdateService : IDesktopUpdateService
    {
        public bool CanSelfUpdate => false;

        public Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReleaseVersion.TryParse(currentVersion, out ReleaseVersion version))
            {
                throw new InvalidOperationException("The test version is invalid.");
            }
            return Task.FromResult(new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                version,
                RuntimeAssetNames.DetectCurrent() ?? "unsupported",
                null,
                "update.current"));
        }

        public Task<SelfUpdateLaunch> InstallAndRestartAsync(UpdateRelease release, string currentVersion, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class NullTerminalIntegrationService : IDesktopTerminalIntegrationService
    {
        public bool IsSupported => true;

        public bool CanInstall => false;

        public string InstallPathDisplay => @"%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe";

        public Task<TerminalIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TerminalIntegrationStatus(
                "socketscope",
                InstallPathDisplay,
                false,
                false,
                false,
                false,
                false));
        }

        public Task<TerminalInstallResult> InstallAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Installation is disabled in the UI smoke test.");
        }
    }

    private sealed class RecordingTerminalIntegrationService : IDesktopTerminalIntegrationService
    {
        public TaskCompletionSource InstallObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSupported => true;

        public bool CanInstall => true;

        public string InstallPathDisplay => @"%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe";

        public int InstallCalls { get; private set; }

        public int StatusCalls { get; private set; }

        public Task<TerminalIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusCalls++;
            return Task.FromResult(new TerminalIntegrationStatus(
                "socketscope",
                InstallPathDisplay,
                true,
                true,
                false,
                true,
                false));
        }

        public Task<TerminalInstallResult> InstallAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallCalls++;
            InstallObserved.TrySetResult();
            return Task.FromResult(new TerminalInstallResult(
                "socketscope",
                InstallPathDisplay,
                true,
                true,
                true,
                false));
        }
    }

    private sealed class ControlledProviders :
        ISocketInventoryProvider,
        IProcessMetadataProvider,
        IServiceAttributionProvider,
        IContainerAttributionProvider,
        INetworkInterfaceProvider,
        IFirewallEvidenceProvider
    {
        private const int ProcessId = 4242;

        public string Name => "controlled-ui";

        public ValueTask<RawInventoryGeneration> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RawSocketRecord listener = new(
                "controlled-listener",
                TransportProtocol.Tcp,
                IpVersion.IPv4,
                SocketRole.TcpListener,
                new IpEndpoint("192.168.50.9", 4567),
                null,
                SocketState.Listen,
                ProcessId,
                now.AddMinutes(-1),
                "controlled-namespace",
                Name);
            ProviderDiagnostic notice = new(
                Name,
                DataAvailability.Partial,
                "controlled.partial",
                "diagnostic-detail-secret 192.168.50.9 secret-process");
            return ValueTask.FromResult(new RawInventoryGeneration(
                now,
                now.AddMilliseconds(5),
                "controlled-namespace",
                [listener],
                [notice],
                false,
                "controlled.partial"));
        }

        public ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Contains(ProcessId, processIds);
            IReadOnlyDictionary<int, ProcessMetadata> result = new Dictionary<int, ProcessMetadata>
            {
                [ProcessId] = new(
                    new ProcessIdentity(ProcessId, DateTimeOffset.UtcNow.AddHours(-1), "controlled-start"),
                    "secret-process",
                    @"C:\Users\ExampleUser\private\secret.exe",
                    DataAvailability.Available),
            };
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            _ = processIds;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<int, ServiceAttribution> result = new Dictionary<int, ServiceAttribution>
            {
                [ProcessId] = new(
                    ProcessId,
                    [new ServiceIdentity("test.service", "Test service", "test", false)],
                    DataAvailability.Available),
            };
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            _ = processIds;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<int, ContainerAttribution> result = new Dictionary<int, ContainerAttribution>
            {
                [ProcessId] = new(
                    ProcessId,
                    new ContainerIdentity("docker", "container-secret-id", "container-secret", "cgroup"),
                    DataAvailability.Available),
            };
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<NetworkInterfaceInfo>> GetInterfacesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<NetworkInterfaceInfo> result =
            [
                new NetworkInterfaceInfo(
                    "controlled-interface",
                    "controlled-interface",
                    InterfaceTechnology.Ethernet,
                    true,
                    [new NetworkInterfaceAddress("192.168.50.9", IpVersion.IPv4, 24)]),
            ];
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
            IReadOnlyList<FirewallQuery> queries,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, FirewallEvaluation> result = queries.ToDictionary(
                query => query.ObservationId,
                _ => new FirewallEvaluation(
                    FirewallAssessment.Partial,
                    [],
                    DataAvailability.Partial,
                    "firewall.controlled-partial"),
                StringComparer.Ordinal);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EmptyProviders :
        ISocketInventoryProvider,
        IProcessMetadataProvider,
        IServiceAttributionProvider,
        IContainerAttributionProvider,
        INetworkInterfaceProvider,
        IFirewallEvidenceProvider
    {
        public string Name => "ui-smoke";

        public ValueTask<RawInventoryGeneration> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new RawInventoryGeneration(now, now, "test", [], [], true));
        }

        public ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            _ = processIds;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyDictionary<int, ProcessMetadata>>(
                new Dictionary<int, ProcessMetadata>());
        }

        public ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            _ = processIds;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyDictionary<int, ServiceAttribution>>(
                new Dictionary<int, ServiceAttribution>());
        }

        public ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            _ = processIds;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyDictionary<int, ContainerAttribution>>(
                new Dictionary<int, ContainerAttribution>());
        }

        public ValueTask<IReadOnlyList<NetworkInterfaceInfo>> GetInterfacesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<NetworkInterfaceInfo>>([]);
        }

        public ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
            IReadOnlyList<FirewallQuery> queries,
            CancellationToken cancellationToken)
        {
            _ = queries;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyDictionary<string, FirewallEvaluation>>(
                new Dictionary<string, FirewallEvaluation>(StringComparer.Ordinal));
        }
    }
}
