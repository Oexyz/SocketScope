using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class ChangesView : UserControl
{
    private readonly DataGridColumnLayout columnLayout;

    public ChangesView()
    {
        AvaloniaXamlLoader.Load(this);
        DataGrid dataGrid = Required<DataGrid>("ChangesDataGrid");
        columnLayout = new DataGridColumnLayout(dataGrid);
        Required<Button>("ResetChangesColumnsButton").Click += (_, _) => columnLayout.Reset();
        LocalizedDataGridHeaders.Bind(
            this,
            dataGrid,
            nameof(UiStrings.DetectedAt),
            nameof(UiStrings.Change),
            nameof(UiStrings.Protocol),
            nameof(UiStrings.LocalAddress),
            nameof(UiStrings.Process),
            nameof(UiStrings.ChangedFields));
        ResponsiveLayout.ConstrainToViewport(
            this,
            this.FindControl<Grid>("LayoutRoot") ?? throw new InvalidOperationException("Changes layout root is missing."),
            1440,
            UpdateResponsiveLayout);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid table = Required<Grid>("ChangesTableContent");
        table.Width = Math.Max(1050, width);

        Grid actions = Required<Grid>("SnapshotActionsGrid");
        Control save = Required<Button>("SaveSnapshotButton");
        Control snapshotA = Required<ComboBox>("SnapshotASelector");
        Control snapshotB = Required<ComboBox>("SnapshotBSelector");
        Control compare = Required<Button>("CompareSnapshotsButton");
        Control status = Required<TextBlock>("SnapshotActionStatus");
        if (width < 1040)
        {
            ResponsiveLayout.SetColumns(actions, ResponsiveLayout.Star(), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(actions, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            Place(save, 0, 0);
            Place(compare, 1, 0);
            Place(snapshotA, 0, 1);
            Place(snapshotB, 1, 1);
            Place(status, 0, 2, columnSpan: 2);
        }
        else
        {
            ResponsiveLayout.SetColumns(
                actions,
                GridLength.Auto,
                new GridLength(240),
                new GridLength(240),
                GridLength.Auto,
                ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(actions, GridLength.Auto);
            Place(save, 0, 0);
            Place(snapshotA, 1, 0);
            Place(snapshotB, 2, 0);
            Place(compare, 3, 0);
            Place(status, 4, 0);
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
}
