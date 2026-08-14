using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OeXYZ.SocketScope.Desktop.Views;

public sealed partial class HelpView : UserControl
{
    public HelpView()
    {
        AvaloniaXamlLoader.Load(this);
        ResponsiveLayout.ConstrainToViewport(
            this,
            Required<Grid>("LayoutRoot"),
            1200,
            UpdateResponsiveLayout);
    }

    private void UpdateResponsiveLayout(double width)
    {
        Grid glossary = Required<Grid>("GlossaryGrid");
        Control[] cards =
        [
            Required<Border>("LocalhostCard"),
            Required<Border>("LanCard"),
            Required<Border>("WildcardCard"),
            Required<Border>("FirewallCandidateCard"),
            Required<Border>("UnknownCard"),
        ];

        if (width < 720)
        {
            ResponsiveLayout.SetColumns(glossary, ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(glossary, [.. Enumerable.Repeat(GridLength.Auto, cards.Length)]);
            for (int index = 0; index < cards.Length; index++)
            {
                Grid.SetColumn(cards[index], 0);
                Grid.SetRow(cards[index], index);
                Grid.SetColumnSpan(cards[index], 1);
            }
        }
        else
        {
            ResponsiveLayout.SetColumns(glossary, ResponsiveLayout.Star(), ResponsiveLayout.Star());
            ResponsiveLayout.SetRows(glossary, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            for (int index = 0; index < cards.Length - 1; index++)
            {
                Grid.SetColumn(cards[index], index % 2);
                Grid.SetRow(cards[index], index / 2);
                Grid.SetColumnSpan(cards[index], 1);
            }

            Grid.SetColumn(cards[^1], 0);
            Grid.SetRow(cards[^1], 2);
            Grid.SetColumnSpan(cards[^1], 2);
        }
    }

    private T Required<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"The {name} control is missing.");
    }
}
