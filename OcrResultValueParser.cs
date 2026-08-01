using System.Globalization;

namespace VRenaResultsCapture;

internal static class OcrResultValueParser
{
    private static readonly HashSet<string> TenPointScoreGames =
        new(StringComparer.Ordinal)
        {
            "laser-tag",
            "office-war",
            "paintball",
            "snow-battle",
            "wild-west",
            "zg-marbles"
        };

    internal static bool TryDecimal(string value, out double parsed) =>
        double.TryParse(
            value.Replace('O', '0').Replace('o', '0').Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);

    internal static bool TryInteger(string value, out int parsed) =>
        int.TryParse(
            value.Replace('O', '0').Replace('o', '0'),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out parsed);

    internal static bool TryAccuracy(string value, out double parsed)
    {
        var normalized = value
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace(',', '.')
            .Trim();

        if (normalized.EndsWith("0/0", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
            if (normalized.Length == 0)
            {
                normalized = "0";
            }
        }
        else if (normalized.EndsWith("/0", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }

        return double.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    internal static bool TryScore(string value, string gameSlug, out int parsed)
    {
        if (!TryInteger(value, out parsed))
        {
            return false;
        }

        if (parsed > 0 && parsed % 10 != 0 && TenPointScoreGames.Contains(gameSlug))
        {
            if (parsed > int.MaxValue / 10)
            {
                return false;
            }

            parsed *= 10;
        }

        return true;
    }
}
