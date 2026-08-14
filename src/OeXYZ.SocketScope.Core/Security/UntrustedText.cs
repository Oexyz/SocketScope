using System.Globalization;
using System.Text;

namespace OeXYZ.SocketScope.Core.Security;

public static class UntrustedText
{
    public const int DefaultMaximumRunes = 4_096;

    public static string ForDisplay(string? value, int maximumRunes = DefaultMaximumRunes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRunes);
        StringBuilder result = new(Math.Min(value.Length, maximumRunes));
        int count = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (count++ >= maximumRunes)
            {
                result.Append('…');
                break;
            }

            if (Rune.IsControl(rune) || IsBidirectionalControl(rune.Value))
            {
                result.Append("\\u");
                result.Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append(rune.ToString());
            }
        }

        return result.ToString();
    }

    private static bool IsBidirectionalControl(int value)
    {
        return value is 0x061C or 0x200E or 0x200F
            or >= 0x202A and <= 0x202E
            or >= 0x2066 and <= 0x2069;
    }
}
