using System.Runtime.InteropServices;

namespace OeXYZ.SocketScope.Platform.Windows;

/// <summary>
/// Hides only a console that Windows allocated solely for a GUI launch. A terminal
/// shared with a shell is retained so the same executable remains a usable CLI.
/// </summary>
public static partial class WindowsConsoleWindow
{
    private const int HideWindow = 0;

    public static unsafe void HideIfOwned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint window = GetConsoleWindow();
        if (window == 0)
        {
            return;
        }

        uint processId = 0;
        uint processCount = GetConsoleProcessList(&processId, 1);
        if (processCount == 1 && processId == (uint)Environment.ProcessId)
        {
            _ = ShowWindow(window, HideWindow);
        }
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint GetConsoleProcessList(uint* processList, uint processCount);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ShowWindow(nint window, int command);
}
