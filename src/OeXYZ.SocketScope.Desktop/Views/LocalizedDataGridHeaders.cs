using Avalonia.Controls;
using Avalonia.Data;

namespace OeXYZ.SocketScope.Desktop.Views;

internal static class LocalizedDataGridHeaders
{
    public static void Bind(UserControl owner, DataGrid dataGrid, params string[] stringProperties)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(stringProperties);

        if (dataGrid.Columns.Count != stringProperties.Length)
        {
            throw new ArgumentException(
                "A localized string property is required for every data-grid column.",
                nameof(stringProperties));
        }

        for (int index = 0; index < stringProperties.Length; index++)
        {
            string property = stringProperties[index];
            if (string.IsNullOrWhiteSpace(property))
            {
                throw new ArgumentException("Localized string property names cannot be empty.", nameof(stringProperties));
            }

            dataGrid.Columns[index].Bind(
                DataGridColumn.HeaderProperty,
                new Binding($"DataContext.Text.{property}")
                {
                    Source = owner,
                });
        }
    }
}
