namespace OeXYZ.SocketScope.Platform.Linux.Storage;

public sealed record LinuxApplicationPaths(
    string Settings,
    string Snapshots,
    string Logs,
    string Updates)
{
    public static LinuxApplicationPaths CreateDefault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Linux did not provide a user home directory.");
        }

        string configBase = AbsoluteEnvironmentPath("XDG_CONFIG_HOME")
            ?? Path.Combine(home, ".config");
        string stateBase = AbsoluteEnvironmentPath("XDG_STATE_HOME")
            ?? Path.Combine(home, ".local", "state");
        string settings = Path.Combine(configBase, "oexyz", "socketscope");
        string state = Path.Combine(stateBase, "oexyz", "socketscope");
        return new LinuxApplicationPaths(
            settings,
            Path.Combine(state, "snapshots"),
            Path.Combine(state, "logs"),
            Path.Combine(state, "updates"));
    }

    private static string? AbsoluteEnvironmentPath(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
            ? Path.GetFullPath(value)
            : null;
    }
}
