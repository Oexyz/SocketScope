using System.Globalization;

namespace OeXYZ.SocketScope.Platform.Linux.Processes;

internal static class ProcProcessStatParser
{
    internal static bool TryParse(
        ReadOnlySpan<char> content,
        int expectedProcessId,
        out string name,
        out ulong startTicks)
    {
        name = string.Empty;
        startTicks = 0;
        int opening = content.IndexOf('(');
        int closing = content.LastIndexOf(')');
        if (opening <= 0 || closing <= opening || closing + 2 >= content.Length)
        {
            return false;
        }

        if (!int.TryParse(
            content[..opening].Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int processId)
            || processId != expectedProcessId)
        {
            return false;
        }

        name = content[(opening + 1)..closing].ToString();
        string[] remaining = content[(closing + 1)..].ToString().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return remaining.Length > 19
            && ulong.TryParse(
                remaining[19],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out startTicks);
    }
}
