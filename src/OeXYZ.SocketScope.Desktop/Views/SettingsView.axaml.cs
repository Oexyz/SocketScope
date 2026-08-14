using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        ResponsiveLayout.ConstrainToViewport(
            this,
            this.FindControl<Grid>("LayoutRoot") ?? throw new InvalidOperationException("Settings layout root is missing."),
            1200,
            UpdateResponsiveLayout);
        Loaded += (_, _) => Dispatcher.UIThread.Post(EnsureInitialSelection, DispatcherPriority.Loaded);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid form = this.FindControl<Grid>("SettingsForm")
            ?? throw new InvalidOperationException("The settings form is missing.");
        Control[] labels =
        [
            this.FindControl<StackPanel>("LanguageLabelPanel")!,
            this.FindControl<StackPanel>("RefreshLabelPanel")!,
            this.FindControl<StackPanel>("UpdateLabelPanel")!,
        ];
        Control[] editors =
        [
            this.FindControl<ComboBox>("LanguageSelector")!,
            this.FindControl<Grid>("RefreshEditor")!,
            this.FindControl<CheckBox>("AutomaticUpdateCheckBox")!,
        ];
        Grid terminalLayout = this.FindControl<Grid>("TerminalStatusLayout")
            ?? throw new InvalidOperationException("The terminal status layout is missing.");
        StackPanel terminalStatus = this.FindControl<StackPanel>("TerminalStatusPanel")
            ?? throw new InvalidOperationException("The terminal status panel is missing.");
        StackPanel terminalActions = this.FindControl<StackPanel>("TerminalActionButtons")
            ?? throw new InvalidOperationException("The terminal action panel is missing.");
        Grid exportProfileGrid = this.FindControl<Grid>("ExportProfileGrid")
            ?? throw new InvalidOperationException("The export profile form is missing.");
        Control exportProfileLabel = this.FindControl<StackPanel>("ExportProfileLabelPanel")
            ?? throw new InvalidOperationException("The export profile label is missing.");
        Control exportProfileSelector = this.FindControl<ComboBox>("ExportProfileSelector")
            ?? throw new InvalidOperationException("The export profile selector is missing.");

        bool stacked = width < 700;
        if (stacked)
        {
            ResponsiveLayout.SetColumns(form, ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(
                form,
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto);
            for (int index = 0; index < labels.Length; index++)
            {
                Grid.SetColumn(labels[index], 0);
                Grid.SetRow(labels[index], index * 2);
                Grid.SetColumn(editors[index], 0);
                Grid.SetRow(editors[index], (index * 2) + 1);
            }
        }
        else
        {
            ResponsiveLayout.SetColumns(form, new GridLength(260), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(form, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            for (int index = 0; index < labels.Length; index++)
            {
                Grid.SetColumn(labels[index], 0);
                Grid.SetRow(labels[index], index);
                Grid.SetColumn(editors[index], 1);
                Grid.SetRow(editors[index], index);
            }
        }

        if (stacked)
        {
            ResponsiveLayout.SetColumns(exportProfileGrid, ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(exportProfileGrid, GridLength.Auto, GridLength.Auto);
            Grid.SetColumn(exportProfileLabel, 0);
            Grid.SetRow(exportProfileLabel, 0);
            Grid.SetColumn(exportProfileSelector, 0);
            Grid.SetRow(exportProfileSelector, 1);
        }
        else
        {
            ResponsiveLayout.SetColumns(exportProfileGrid, new GridLength(260), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(exportProfileGrid, GridLength.Auto);
            Grid.SetColumn(exportProfileLabel, 0);
            Grid.SetRow(exportProfileLabel, 0);
            Grid.SetColumn(exportProfileSelector, 1);
            Grid.SetRow(exportProfileSelector, 0);
        }

        ResponsiveLayout.SetColumns(terminalLayout, ResponsiveLayout.Star());
        ResponsiveLayout.SetRows(terminalLayout, GridLength.Auto, GridLength.Auto);
        Grid.SetColumn(terminalStatus, 0);
        Grid.SetRow(terminalStatus, 0);
        Grid.SetColumn(terminalActions, 0);
        Grid.SetRow(terminalActions, 1);
        terminalActions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        terminalActions.Orientation = width < 560
            ? Avalonia.Layout.Orientation.Vertical
            : Avalonia.Layout.Orientation.Horizontal;
    }

    private void EnsureInitialSelection()
    {
        EnsureSelection("LanguageSelector");
        EnsureSelection("ExportProfileSelector");
    }

    private void EnsureSelection(string name)
    {
        ComboBox comboBox = this.FindControl<ComboBox>(name)
            ?? throw new InvalidOperationException($"The {name} selector is missing.");
        if (comboBox.SelectedIndex < 0 && comboBox.ItemCount > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
}
