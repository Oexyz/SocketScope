using System.Windows.Input;

namespace OeXYZ.SocketScope.Desktop.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(
    Func<CancellationToken, Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onError = null) : ICommand
{
    private bool isRunning;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isRunning && (canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        _ = ExecuteCoreAsync();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private async Task ExecuteCoreAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            onError?.Invoke(exception);
        }
        finally
        {
            isRunning = false;
            RaiseCanExecuteChanged();
        }
    }
}
