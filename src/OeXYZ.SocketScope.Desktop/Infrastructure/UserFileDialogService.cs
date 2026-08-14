using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public interface IUserFileDialogService
{
    Task<string?> SelectReportTargetAsync(string suggestedName, bool html);
}

public sealed class UserFileDialogService : IUserFileDialogService
{
    private Window? owner;

    public void Attach(Window window)
    {
        owner = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<string?> SelectReportTargetAsync(string suggestedName, bool html)
    {
        if (owner?.StorageProvider is not { CanSave: true } storageProvider)
        {
            return null;
        }

        FilePickerFileType type = new(html ? "HTML report" : "JSON report")
        {
            Patterns = html ? ["*.html"] : ["*.json"],
            MimeTypes = html ? ["text/html"] : ["application/json"],
        };
        IStorageFile? selected = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = html ? "html" : "json",
            FileTypeChoices = [type],
            ShowOverwritePrompt = true,
        });
        return selected?.TryGetLocalPath();
    }
}
