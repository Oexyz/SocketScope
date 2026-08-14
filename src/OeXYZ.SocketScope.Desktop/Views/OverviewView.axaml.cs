using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class OverviewView : UserControl
{
    private readonly DataGridColumnLayout columnLayout;

    public OverviewView()
    {
        AvaloniaXamlLoader.Load(this);
        DataGrid dataGrid = RequiredGrid("OverviewDataGrid");
        columnLayout = new DataGridColumnLayout(dataGrid);
        this.FindControl<Button>("ResetOverviewColumnsButton")!.Click += (_, _) => columnLayout.Reset();
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
            this.FindControl<Grid>("LayoutRoot") ?? throw new InvalidOperationException("Overview layout root is missing."),
            1440,
            UpdateResponsiveLayout);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid table = this.FindControl<Grid>("OverviewTableContent")
            ?? throw new InvalidOperationException("The overview table is missing.");
        table.Width = Math.Max(930, width);

        Grid summary = this.FindControl<Grid>("SummaryGrid")
            ?? throw new InvalidOperationException("The summary grid is missing.");
        Control[] cards =
        [
            RequiredCard("ActiveConnectionsCard"),
            RequiredCard("LocalListenersCard"),
            RequiredCard("LanListenersCard"),
            RequiredCard("WildcardListenersCard"),
            RequiredCard("NewObservationsCard"),
        ];

        int columns = width >= 1040 ? 5 : width >= 680 ? 3 : 2;
        ResponsiveLayout.SetColumns(summary, [.. Enumerable.Repeat(ResponsiveLayout.Star(), columns)]);
        int rows = (int)Math.Ceiling(cards.Length / (double)columns);
        ResponsiveLayout.SetRows(summary, [.. Enumerable.Repeat(GridLength.Auto, rows)]);
        for (int index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], index % columns);
            Grid.SetRow(cards[index], index / columns);
        }
    }

    private Border RequiredCard(string name)
    {
        return this.FindControl<Border>(name)
            ?? throw new InvalidOperationException($"The {name} card is missing.");
    }

    private DataGrid RequiredGrid(string name)
    {
        return this.FindControl<DataGrid>(name)
            ?? throw new InvalidOperationException($"The {name} data grid is missing.");
    }
}
