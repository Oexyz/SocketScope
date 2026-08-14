using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class InventoryView : UserControl
{
    private readonly DataGridColumnLayout columnLayout;

    public InventoryView()
    {
        AvaloniaXamlLoader.Load(this);
        DataGrid dataGrid = Required<DataGrid>("InventoryDataGrid");
        columnLayout = new DataGridColumnLayout(dataGrid);
        Required<Button>("ResetInventoryColumnsButton").Click += (_, _) => columnLayout.Reset();
        LocalizedDataGridHeaders.Bind(
            this,
            dataGrid,
            nameof(UiStrings.Protocol),
            nameof(UiStrings.LocalAddress),
            nameof(UiStrings.RemoteAddress),
            nameof(UiStrings.State),
            nameof(UiStrings.Process),
            nameof(UiStrings.Pid),
            nameof(UiStrings.Scope));
        ResponsiveLayout.ConstrainToViewport(
            this,
            this.FindControl<Grid>("LayoutRoot") ?? throw new InvalidOperationException("Inventory layout root is missing."),
            1440,
            UpdateResponsiveLayout);
        Loaded += (_, _) => Dispatcher.UIThread.Post(EnsureInitialSelections, DispatcherPriority.Loaded);
    }

    public void FocusSearch()
    {
        Required<TextBox>("InventorySearch").Focus();
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid filters = Required<Grid>("FilterGrid");
        Control search = Required<TextBox>("InventorySearch");
        Control protocol = Required<ComboBox>("ProtocolFilter");
        Control state = Required<ComboBox>("StateFilter");
        Control scope = Required<ComboBox>("ScopeFilter");
        Control sort = Required<ComboBox>("SortFilter");
        Control direction = Required<Button>("SortDirectionButton");

        if (width >= 1080)
        {
            ResponsiveLayout.SetColumns(
                filters,
                ResponsiveLayout.Star(),
                new GridLength(145),
                new GridLength(160),
                new GridLength(175),
                new GridLength(155),
                new GridLength(42));
            ResponsiveLayout.SetRows(filters, GridLength.Auto);
            Control[] controls = [search, protocol, state, scope, sort, direction];
            for (int index = 0; index < controls.Length; index++)
            {
                Grid.SetColumn(controls[index], index);
                Grid.SetRow(controls[index], 0);
                Grid.SetColumnSpan(controls[index], 1);
            }
        }
        else
        {
            ResponsiveLayout.SetColumns(filters, ResponsiveLayout.Star(), ResponsiveLayout.Star(), new GridLength(42));
            ResponsiveLayout.SetRows(filters, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            Grid.SetColumn(search, 0);
            Grid.SetRow(search, 0);
            Grid.SetColumnSpan(search, 3);
            Place(protocol, 0, 1);
            Place(state, 1, 1, columnSpan: 2);
            Place(scope, 0, 2);
            Place(sort, 1, 2);
            Place(direction, 2, 2);
        }

        Grid table = Required<Grid>("InventoryTableContent");
        table.Width = Math.Max(910, width);

        Grid details = Required<Grid>("DetailsGrid");
        Control endpoint = Required<StackPanel>("EndpointDetailsPanel");
        Control process = Required<StackPanel>("ProcessDetailsPanel");
        Control binding = Required<StackPanel>("BindingDetailsPanel");
        if (width < 850)
        {
            ResponsiveLayout.SetColumns(details, ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(details, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            Place(endpoint, 0, 0);
            Place(process, 0, 1);
            Place(binding, 0, 2);
        }
        else
        {
            ResponsiveLayout.SetColumns(details, new GridLength(210), ResponsiveLayout.Star(), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(details, GridLength.Auto);
            Place(endpoint, 0, 0);
            Place(process, 1, 0);
            Place(binding, 2, 0);
        }
    }

    private static void Place(Control control, int column, int row, int columnSpan = 1)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, columnSpan);
    }

    private T Required<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"The {name} control is missing.");
    }

    private void EnsureInitialSelections()
    {
        EnsureInitialSelection("ProtocolFilter");
        EnsureInitialSelection("StateFilter");
        EnsureInitialSelection("ScopeFilter");
        EnsureInitialSelection("SortFilter");
    }

    private void EnsureInitialSelection(string controlName)
    {
        ComboBox comboBox = this.FindControl<ComboBox>(controlName)
            ?? throw new InvalidOperationException($"The {controlName} combo box is missing.");
        if (comboBox.SelectedIndex < 0 && comboBox.ItemCount > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
}
