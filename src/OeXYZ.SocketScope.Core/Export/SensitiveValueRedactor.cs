using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OeXYZ.SocketScope.Core.Export;

public static class SensitiveValuePolicy
{
    public const int MaximumValueCount = 32;
    public const int MinimumValueLength = 3;
    public const int MaximumValueLength = 128;
    public const int MaximumCombinedLength = 2_048;

    public static ImmutableArray<string> Normalize(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ImmutableArray<string>.Builder normalized = ImmutableArray.CreateBuilder<string>();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int combinedLength = 0;
        foreach (string? candidate in values)
        {
            if (candidate is null)
            {
                throw new ArgumentException("Sensitive values cannot contain null entries.", nameof(values));
            }

            string value = candidate.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (value.Length is < MinimumValueLength or > MaximumValueLength)
            {
                throw new ArgumentException(
                    $"Each sensitive value must contain {MinimumValueLength} to {MaximumValueLength} characters.",
                    nameof(values));
            }

            if (value.Any(char.IsControl))
            {
                throw new ArgumentException("Sensitive values cannot contain control characters.", nameof(values));
            }

            if (!seen.Add(value))
            {
                continue;
            }

            combinedLength += value.Length;
            if (normalized.Count >= MaximumValueCount || combinedLength > MaximumCombinedLength)
            {
                throw new ArgumentException(
                    $"At most {MaximumValueCount} sensitive values and {MaximumCombinedLength} characters are supported.",
                    nameof(values));
            }

            normalized.Add(value);
        }

        return normalized.ToImmutable();
    }

    public static bool IsCanonical(ImmutableArray<string> values)
    {
        if (values.IsDefault)
        {
            return false;
        }

        try
        {
            ImmutableArray<string> normalized = Normalize(values);
            return normalized.SequenceEqual(values, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed class SensitiveValueRedactor
{
    private readonly ImmutableArray<string> values;

    public SensitiveValueRedactor(ImmutableArray<string> configuredValues)
    {
        if (!SensitiveValuePolicy.IsCanonical(configuredValues))
        {
            throw new ArgumentException("Sensitive values are not canonical.", nameof(configuredValues));
        }

        values = [.. configuredValues
            .OrderByDescending(value => value.Length)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)];
    }

    public int ConfiguredValueCount => values.Length;

    public int CountMatches(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Scan(value, null, null);
    }

    public string Redact(string value, byte[] salt, out int matchCount)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(salt);

        StringBuilder? output = null;
        matchCount = Scan(value, salt, builder => output = builder);
        return output?.ToString() ?? value;
    }

    private int Scan(string value, byte[]? salt, Action<StringBuilder>? setOutput)
    {
        if (values.Length == 0 || value.Length == 0)
        {
            return 0;
        }

        StringBuilder? output = null;
        int matchCount = 0;
        int index = 0;
        int segmentStart = 0;
        while (index < value.Length)
        {
            string? matched = null;
            foreach (string configured in values)
            {
                if (value.AsSpan(index).StartsWith(configured, StringComparison.OrdinalIgnoreCase))
                {
                    matched = configured;
                    break;
                }
            }

            if (matched is null)
            {
                index++;
                continue;
            }

            matchCount++;
            if (salt is not null)
            {
                output ??= new StringBuilder(value.Length + 16);
                output.Append(value, segmentStart, index - segmentStart);
                output.Append(Token(matched, salt));
            }

            index += matched.Length;
            segmentStart = index;
        }

        if (output is not null)
        {
            output.Append(value, segmentStart, value.Length - segmentStart);
            setOutput?.Invoke(output);
        }

        return matchCount;
    }

    private static string Token(string value, byte[] salt)
    {
        byte[] digest = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(value));
        return $"<SENSITIVE-{Convert.ToHexString(digest.AsSpan(0, 4))}>";
    }
}
