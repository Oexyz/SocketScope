namespace OeXYZ.SocketScope.Cli;

internal sealed class CliUsageException(string message) : Exception(message);

internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> values;
    private readonly HashSet<string> flags;

    private CliArguments(
        Dictionary<string, List<string>> values,
        HashSet<string> flags,
        IReadOnlyList<string> positionals)
    {
        this.values = values;
        this.flags = flags;
        Positionals = positionals;
    }

    internal IReadOnlyList<string> Positionals { get; }

    internal static CliArguments Parse(
        IReadOnlyList<string> arguments,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> flagOptions)
    {
        Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
        HashSet<string> flags = new(StringComparer.Ordinal);
        List<string> positionals = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                if (token.StartsWith('-'))
                {
                    throw new CliUsageException($"Unknown option '{token}'.");
                }

                positionals.Add(token);
                continue;
            }

            int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
            string name = equalsIndex < 0 ? token : token[..equalsIndex];
            if (flagOptions.Contains(name))
            {
                if (equalsIndex >= 0)
                {
                    throw new CliUsageException($"Option '{name}' does not accept a value.");
                }

                flags.Add(name);
                continue;
            }

            if (!valueOptions.Contains(name))
            {
                throw new CliUsageException($"Unknown option '{name}'.");
            }

            string value;
            if (equalsIndex >= 0)
            {
                value = token[(equalsIndex + 1)..];
            }
            else if (++index < arguments.Count)
            {
                value = arguments[index];
            }
            else
            {
                throw new CliUsageException($"Option '{name}' requires a value.");
            }

            if (string.IsNullOrEmpty(value))
            {
                throw new CliUsageException($"Option '{name}' requires a non-empty value.");
            }

            if (!values.TryGetValue(name, out List<string>? existing))
            {
                existing = [];
                values[name] = existing;
            }

            existing.Add(value);
        }

        return new CliArguments(values, flags, positionals);
    }

    internal bool HasFlag(string name) => flags.Contains(name);

    internal string? GetSingle(string name)
    {
        if (!values.TryGetValue(name, out List<string>? found))
        {
            return null;
        }

        if (found.Count != 1)
        {
            throw new CliUsageException($"Option '{name}' may only be specified once.");
        }

        return found[0];
    }

    internal IReadOnlyList<string> GetMany(string name)
    {
        return values.TryGetValue(name, out List<string>? found) ? found : [];
    }
}
