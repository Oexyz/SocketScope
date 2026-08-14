using Avalonia.Controls;
using Avalonia.Layout;

namespace OeXYZ.SocketScope.Desktop.Views;

internal static class ResponsiveLayout
{
    internal static void ConstrainToViewport(
        UserControl owner,
        Control content,
        double maximumWidth,
        Action<double>? widthChanged = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        if (!double.IsFinite(maximumWidth) || maximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        content.MaxWidth = maximumWidth;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Top;
        content.SizeChanged += (_, eventArgs) => widthChanged?.Invoke(eventArgs.NewSize.Width);
        owner.AttachedToVisualTree += (_, _) => widthChanged?.Invoke(content.Bounds.Width);
    }

    internal static void SetColumns(Grid grid, params GridLength[] widths)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.ColumnDefinitions.Clear();
        foreach (GridLength width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }
    }

    internal static void SetRows(Grid grid, params GridLength[] heights)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.RowDefinitions.Clear();
        foreach (GridLength height in heights)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        }
    }

    internal static GridLength Star(double value = 1) => new(value, GridUnitType.Star);
}
