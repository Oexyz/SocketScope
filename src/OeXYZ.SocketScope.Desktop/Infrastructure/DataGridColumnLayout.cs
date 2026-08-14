using Avalonia.Controls;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

internal sealed class DataGridColumnLayout
{
    private readonly ColumnState[] defaults;

    public DataGridColumnLayout(DataGrid dataGrid)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        defaults = [.. dataGrid.Columns.Select(column => new ColumnState(
            column,
            column.DisplayIndex,
            column.Width))];
    }

    public void Reset()
    {
        foreach (ColumnState state in defaults.OrderBy(state => state.DisplayIndex))
        {
            state.Column.DisplayIndex = state.DisplayIndex;
        }

        foreach (ColumnState state in defaults)
        {
            state.Column.Width = state.Width;
        }
    }

    private sealed record ColumnState(
        DataGridColumn Column,
        int DisplayIndex,
        DataGridLength Width);
}
