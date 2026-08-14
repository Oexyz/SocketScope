using System.Text;

namespace OeXYZ.SocketScope.Platform.Linux.Procfs;

internal static class ProcFileReader
{
    internal static async ValueTask<string> ReadTextAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4_096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[Math.Min(maximumBytes + 1, 16_384)];
        using MemoryStream content = new(Math.Min(maximumBytes, 16_384));
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > maximumBytes)
            {
                throw new InvalidDataException("A procfs field exceeds the supported size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(content.GetBuffer(), 0, checked((int)content.Length));
    }
}
