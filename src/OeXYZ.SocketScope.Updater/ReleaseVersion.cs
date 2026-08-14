using System.Globalization;

namespace OeXYZ.SocketScope.Updater;

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> candidate = value.AsSpan().Trim();
        if (candidate.Length > 0 && candidate[0] is 'v' or 'V')
        {
            candidate = candidate[1..];
        }

        Span<Range> ranges = stackalloc Range[4];
        int count = candidate.Split(ranges, '.', StringSplitOptions.None);
        if (count != 3)
        {
            return false;
        }

        if (!TryParseComponent(candidate[ranges[0]], out int major)
            || !TryParseComponent(candidate[ranges[1]], out int minor)
            || !TryParseComponent(candidate[ranges[2]], out int patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        return result == 0 ? Patch.CompareTo(other.Patch) : result;
    }

    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}");
    }

    private static bool TryParseComponent(ReadOnlySpan<char> value, out int component)
    {
        component = 0;
        if (value.IsEmpty || (value.Length > 1 && value[0] == '0'))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component);
    }
}
