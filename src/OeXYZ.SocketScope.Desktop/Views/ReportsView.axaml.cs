using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class ReportsView : UserControl
{
    public ReportsView()
    {
        AvaloniaXamlLoader.Load(this);
        ResponsiveLayout.ConstrainToViewport(
            this,
            this.FindControl<Grid>("LayoutRoot") ?? throw new InvalidOperationException("Reports layout root is missing."),
            1440,
            UpdateResponsiveLayout);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid choices = this.FindControl<Grid>("ReportChoicesGrid")
            ?? throw new InvalidOperationException("Report choices are missing.");
        Border full = this.FindControl<Border>("FullReportCard")
            ?? throw new InvalidOperationException("The full report card is missing.");
        Border shareSafe = this.FindControl<Border>("ShareSafeReportCard")
            ?? throw new InvalidOperationException("The share-safe report card is missing.");
        StackPanel[] buttonRows =
        [
            this.FindControl<StackPanel>("DefaultExportButtons")!,
            this.FindControl<StackPanel>("FullExportButtons")!,
            this.FindControl<StackPanel>("ShareSafeExportButtons")!,
        ];

        foreach (StackPanel buttons in buttonRows)
        {
            buttons.Orientation = width < 560
                ? Avalonia.Layout.Orientation.Vertical
                : Avalonia.Layout.Orientation.Horizontal;
        }

        if (width < 760)
        {
            ResponsiveLayout.SetColumns(choices, ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(choices, GridLength.Auto, GridLength.Auto);
            Grid.SetColumn(full, 0);
            Grid.SetRow(full, 0);
            Grid.SetColumn(shareSafe, 0);
            Grid.SetRow(shareSafe, 1);
        }
        else
        {
            ResponsiveLayout.SetColumns(choices, ResponsiveLayout.Star(), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(choices, GridLength.Auto);
            Grid.SetColumn(full, 0);
            Grid.SetRow(full, 0);
            Grid.SetColumn(shareSafe, 1);
            Grid.SetRow(shareSafe, 0);
        }
    }
}
