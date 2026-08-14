using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public interface IDesktopClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}

public sealed class DesktopClipboardService : IDesktopClipboardService
{
    internal const int MaximumClipboardCharacters = 1_048_576;

    private Window? owner;

    public void Attach(Window window)
    {
        owner = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumClipboardCharacters)
        {
            throw new InvalidOperationException("The clipboard text exceeds the supported size limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        IClipboard clipboard = owner?.Clipboard
            ?? throw new InvalidOperationException("The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
