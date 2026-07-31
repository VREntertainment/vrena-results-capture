using System.Globalization;
using System.Security.Cryptography;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace VRenaResultsCapture;

internal static partial class WindowsResultReader
{
    private const int OcrMaximumDimension = 2400;

    private static readonly IReadOnlyDictionary<string, (string Name, string Slug)> Games =
        new Dictionary<string, (string Name, string Slug)>(StringComparer.OrdinalIgnoreCase)
        {
            ["lasertag"] = ("Laser Tag", "laser-tag"),
            ["mbtowers"] = ("Mini Block Towers", "mini-block-towers"),
            ["miniblocktowers"] = ("Mini Block Towers", "mini-block-towers"),
            ["officewar"] = ("Office War", "office-war"),
            ["paintball"] = ("Paintball", "paintball"),
            ["snowbattle"] = ("Snow Battle", "snow-battle"),
            ["castleunspunnen"] = ("Castle Unspunnen", "castle-unspunnen"),
            ["wildwest"] = ("WildWest", "wild-west"),
            ["arcofthecovenant"] = ("The Secret of the Arc", "arc-of-the-covenant"),
            ["secretarc"] = ("The Secret of the Arc", "arc-of-the-covenant"),
            ["jollerhouse"] = ("Joller House", "joller-house")
        };

    internal static async Task<ResultReadOutcome> ReadAsync(
        string screenshotPath,
        DateTimeOffset fallbackCapturedAt)
    {
        var captureId = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(screenshotPath))).ToLowerInvariant();

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            throw new InvalidOperationException(
                "Windows OCR is unavailable. Install the English language OCR feature in Windows Settings.");
        }

        using var screenshot = new Bitmap(screenshotPath);
        var passes = new List<OcrPass>
        {
            await RecognizeRegionAsync(engine, screenshot, "full", new RectangleF(0, 0, 1, 1)),
            await RecognizeRegionAsync(engine, screenshot, "header", new RectangleF(0.22f, 0.06f, 0.56f, 0.24f)),
            await RecognizeRegionAsync(engine, screenshot, "left-table", new RectangleF(0.05f, 0.20f, 0.50f, 0.42f)),
            await RecognizeRegionAsync(engine, screenshot, "right-table", new RectangleF(0.45f, 0.20f, 0.50f, 0.42f)),
            await RecognizeRegionAsync(engine, screenshot, "left-player-row-1", new RectangleF(0.02f, 0.25f, 0.47f, 0.17f)),
            await RecognizeRegionAsync(engine, screenshot, "left-player-row-2", new RectangleF(0.02f, 0.34f, 0.47f, 0.17f)),
            await RecognizeRegionAsync(engine, screenshot, "right-player-row-1", new RectangleF(0.51f, 0.25f, 0.47f, 0.17f)),
            await RecognizeRegionAsync(engine, screenshot, "right-player-row-2", new RectangleF(0.51f, 0.34f, 0.47f, 0.17f)),
            await RecognizeRegionAsync(engine, screenshot, "game-footer", new RectangleF(0, 0.90f, 0.25f, 0.10f)),
            await RecognizeRegionAsync(engine, screenshot, "game", new RectangleF(0, 0.72f, 0.48f, 0.28f))
        };

        var diagnosticText = BuildDiagnosticText(passes);
        DiagnosticLog.SaveOcrText(captureId, diagnosticText);
        var fullText = string.Join(
            Environment.NewLine,
            passes.Select(pass => pass.Text));

        var gameText = string.Join(
            Environment.NewLine,
            passes
                .OrderBy(pass => pass.Name.Equals("game-footer", StringComparison.Ordinal)
                    ? 0
                    : pass.Name.Equals("game", StringComparison.Ordinal) ? 1 : 2)
                .Select(pass => pass.Text));
        var game = FindGame(gameText);
        if (game is null)
        {
            DiagnosticLog.Warning($"OCR did not identify a supported game. CaptureId={captureId}");
            return new ResultReadOutcome(
                captureId,
                diagnosticText,
                null,
                "game_not_recognized");
        }

        var players = passes
            .Where(pass =>
                pass.Name.Contains("player-row", StringComparison.Ordinal) ||
                pass.Name is "left-table" or "right-table" or "full")
            .OrderBy(pass => pass.Name.Contains("player-row", StringComparison.Ordinal) ? 0 : 1)
            .SelectMany(pass => CandidateRows(pass.Result))
            .Select(row => ParsePlayerLine(row, game.Value.Slug))
            .Where(player => player is not null)
            .Cast<RecognizedPlayer>()
            .GroupBy(player => player.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(4)
            .ToList();

        if (players.Count == 0)
        {
            DiagnosticLog.Warning($"OCR did not identify any complete player row. CaptureId={captureId}");
            return new ResultReadOutcome(
                captureId,
                diagnosticText,
                null,
                "players_not_recognized");
        }

        DiagnosticLog.Info(
            $"OCR recognized result. CaptureId={captureId}; Game={game.Value.Slug}; " +
            $"Players={players.Count}; Session={ParseSessionLabel(fullText) ?? "none"}");
        return new ResultReadOutcome(
            captureId,
            diagnosticText,
            new RecognizedResult
            {
                CaptureId = captureId,
                CapturedAt = ParseDisplayedTimestamp(fullText, fallbackCapturedAt),
                DeviceName = Environment.MachineName,
                ExternalSessionLabel = ParseSessionLabel(fullText),
                GameName = game.Value.Name,
                GameSlug = game.Value.Slug,
                Players = players
            },
            null);
    }

    private static async Task<OcrPass> RecognizeRegionAsync(
        OcrEngine engine,
        Bitmap screenshot,
        string name,
        RectangleF fractionalRegion)
    {
        using var prepared = PrepareRegion(screenshot, fractionalRegion);
        await using var stream = new MemoryStream();
        prepared.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        using var randomAccessStream = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        var text = string.Join(
            Environment.NewLine,
            result.Lines.Select(line => JoinWords(line.Words)));
        return new OcrPass(name, result, text);
    }

    private static Bitmap PrepareRegion(Bitmap screenshot, RectangleF fractionalRegion)
    {
        var x = Math.Clamp(
            (int)Math.Floor(screenshot.Width * fractionalRegion.X),
            0,
            Math.Max(0, screenshot.Width - 1));
        var y = Math.Clamp(
            (int)Math.Floor(screenshot.Height * fractionalRegion.Y),
            0,
            Math.Max(0, screenshot.Height - 1));
        var width = Math.Clamp(
            (int)Math.Ceiling(screenshot.Width * fractionalRegion.Width),
            1,
            screenshot.Width - x);
        var height = Math.Clamp(
            (int)Math.Ceiling(screenshot.Height * fractionalRegion.Height),
            1,
            screenshot.Height - y);

        var scale = Math.Min(
            3d,
            Math.Max(
                1d,
                Math.Min(
                    (double)OcrMaximumDimension / width,
                    (double)OcrMaximumDimension / height)));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        var prepared = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(prepared);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.DrawImage(
            screenshot,
            new Rectangle(0, 0, targetWidth, targetHeight),
            new Rectangle(x, y, width, height),
            GraphicsUnit.Pixel);
        return prepared;
    }

    private static string BuildDiagnosticText(IEnumerable<OcrPass> passes)
    {
        var output = new StringBuilder();
        foreach (var pass in passes)
        {
            output.Append('[').Append(pass.Name).AppendLine("]");
            output.AppendLine(pass.Text);
            output.AppendLine("[spatial-rows]");
            foreach (var row in SpatialRows(pass.Result))
            {
                output.AppendLine(row);
            }

            output.AppendLine();
        }

        return output.ToString().TrimEnd();
    }

    private static IEnumerable<string> CandidateRows(OcrResult result) =>
        result.Lines
            .Select(line => JoinWords(line.Words))
            .Concat(SpatialRows(result))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SpatialRows(OcrResult result)
    {
        var rows = new List<SpatialWordRow>();
        var words = result.Lines
            .SelectMany(line => line.Words)
            .OrderBy(word => word.BoundingRect.Y + word.BoundingRect.Height / 2d)
            .ThenBy(word => word.BoundingRect.X);

        foreach (var word in words)
        {
            var centerY = word.BoundingRect.Y + word.BoundingRect.Height / 2d;
            var matchingRow = rows
                .Select(row => new
                {
                    Row = row,
                    Distance = Math.Abs(row.CenterY - centerY),
                    Tolerance = Math.Max(8d, Math.Max(row.AverageHeight, word.BoundingRect.Height) * 0.65d)
                })
                .Where(candidate => candidate.Distance <= candidate.Tolerance)
                .OrderBy(candidate => candidate.Distance)
                .Select(candidate => candidate.Row)
                .FirstOrDefault();

            if (matchingRow is null)
            {
                matchingRow = new SpatialWordRow();
                rows.Add(matchingRow);
            }

            matchingRow.Words.Add(word);
        }

        return rows
            .OrderBy(row => row.CenterY)
            .Select(row => JoinWords(row.Words.OrderBy(word => word.BoundingRect.X)))
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .ToList();
    }

    private static string JoinWords(IEnumerable<OcrWord> words) =>
        string.Join(" ", words.Select(word => word.Text));

    private static RecognizedPlayer? ParsePlayerLine(string source, string gameSlug)
    {
        var line = Regex.Replace(source, @"\s+", " ").Trim();
        if (gameSlug.Equals("mini-block-towers", StringComparison.Ordinal))
        {
            var miniBlockTowersPlayer = ParseMiniBlockTowersPlayerLine(line);
            if (miniBlockTowersPlayer is not null)
            {
                return miniBlockTowersPlayer;
            }
        }

        var match = PlayerLinePattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var name = CleanPlayerName(match.Groups["name"].Value);
        if (name is null)
        {
            return null;
        }

        if (!TryOcrInteger(match.Groups["hits"].Value, out var hits) ||
            !TryOcrInteger(match.Groups["score"].Value, out var score) ||
            !TryOcrAccuracy(match.Groups["accuracy"].Value, out var accuracy) ||
            !TryDecimal(match.Groups["movement"].Value, out var movement))
        {
            return null;
        }

        if (hits < 0 || score < 0 || accuracy is < 0 or > 100 || movement < 0)
        {
            return null;
        }

        return new RecognizedPlayer
        {
            Name = name,
            Hits = hits,
            AccuracyPercent = accuracy,
            MovementMeters = movement,
            Score = score
        };
    }

    private static RecognizedPlayer? ParseMiniBlockTowersPlayerLine(string line)
    {
        var match = MiniBlockTowersPlayerLinePattern().Match(line);
        if (!match.Success)
        {
            var zeroScoreMatch = MiniBlockTowersZeroScorePlayerLinePattern().Match(line);
            if (!zeroScoreMatch.Success ||
                !TryOcrInteger(zeroScoreMatch.Groups["hits"].Value, out var zeroHits) ||
                !TryOcrInteger(zeroScoreMatch.Groups["shield"].Value, out var zeroShield) ||
                !TryOcrInteger(zeroScoreMatch.Groups["towers"].Value, out var zeroTowers) ||
                zeroHits != 0 || zeroShield != 0 || zeroTowers != 0)
            {
                return null;
            }

            var zeroScoreName = CleanPlayerName(zeroScoreMatch.Groups["name"].Value);
            return zeroScoreName is null
                ? null
                : new RecognizedPlayer
                {
                    Name = zeroScoreName,
                    Hits = 0,
                    AccuracyPercent = null,
                    MovementMeters = null,
                    Score = 0
                };
        }

        var name = CleanPlayerName(match.Groups["name"].Value);
        if (name is null ||
            !TryOcrInteger(match.Groups["hits"].Value, out var hits) ||
            !TryOcrInteger(match.Groups["shield"].Value, out var shield) ||
            !TryOcrInteger(match.Groups["towers"].Value, out var towers) ||
            !TryOcrInteger(match.Groups["score"].Value, out var score) ||
            hits < 0 || shield < 0 || towers < 0 || score < 0)
        {
            return null;
        }

        return new RecognizedPlayer
        {
            Name = name,
            Hits = hits,
            AccuracyPercent = null,
            MovementMeters = null,
            Score = score
        };
    }

    private static string? CleanPlayerName(string value)
    {
        var name = value.Trim(' ', '"', '“', '”');
        name = LeadingCrownArtifactPattern().Replace(name, string.Empty).Trim();
        name = DefaultPlayerNamePattern().Replace(name, "Player$1");
        if (name.Length is < 1 or > 80 ||
            name.Equals("Team", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(name, @"\b\d+(?:[\.,]\d+)?\s*m\b", RegexOptions.IgnoreCase) ||
            Regex.Matches(name, @"(?:^|\s)(?:\d+(?:[\.,]\d+)?|[Oo])(?=\s|$)").Count > 1)
        {
            return null;
        }

        return name;
    }

    private static bool TryDecimal(string value, out double parsed) =>
        double.TryParse(
            value.Replace('O', '0').Replace('o', '0').Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);

    private static bool TryOcrInteger(string value, out int parsed) =>
        int.TryParse(
            value.Replace('O', '0').Replace('o', '0'),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out parsed);

    private static bool TryOcrAccuracy(string value, out double parsed)
    {
        var normalized = value
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace(',', '.')
            .Replace("/0", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return double.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static (string Name, string Slug)? FindGame(string text)
    {
        var normalized = Regex.Replace(text, @"[^A-Za-z0-9]", string.Empty);
        foreach (var game in Games.OrderByDescending(item => item.Key.Length))
        {
            if (normalized.Contains(game.Key, StringComparison.OrdinalIgnoreCase))
            {
                return game.Value;
            }
        }

        var tokens = Regex.Matches(text, @"[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToList();
        var candidates = new List<string>(tokens);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (index + 1 < tokens.Count)
            {
                candidates.Add(tokens[index] + tokens[index + 1]);
            }

            if (index + 2 < tokens.Count)
            {
                candidates.Add(tokens[index] + tokens[index + 1] + tokens[index + 2]);
            }
        }

        foreach (var game in Games.OrderByDescending(item => item.Key.Length))
        {
            var allowedDistance = Math.Max(1, game.Key.Length / 6);
            if (candidates.Any(candidate =>
                    Math.Abs(candidate.Length - game.Key.Length) <= allowedDistance &&
                    EditDistance(candidate, game.Key) <= allowedDistance))
            {
                return game.Value;
            }
        }

        return null;
    }

    private static int EditDistance(string first, string second)
    {
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var current = new int[second.Length + 1];

        for (var firstIndex = 1; firstIndex <= first.Length; firstIndex++)
        {
            current[0] = firstIndex;
            for (var secondIndex = 1; secondIndex <= second.Length; secondIndex++)
            {
                var substitutionCost = char.ToUpperInvariant(first[firstIndex - 1]) ==
                    char.ToUpperInvariant(second[secondIndex - 1])
                    ? 0
                    : 1;
                current[secondIndex] = Math.Min(
                    Math.Min(
                        current[secondIndex - 1] + 1,
                        previous[secondIndex] + 1),
                    previous[secondIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }

    private static string? ParseSessionLabel(string text)
    {
        var match = SessionPattern().Match(text);
        return match.Success ? $"Session {match.Groups["session"].Value}" : null;
    }

    private static DateTimeOffset ParseDisplayedTimestamp(string text, DateTimeOffset fallback)
    {
        var match = TimestampPattern().Match(text);
        if (!match.Success)
        {
            return fallback;
        }

        var value = $"{match.Groups["date"].Value} {match.Groups["time"].Value}";
        if (!DateTime.TryParseExact(
                value,
                ["dd.MM.yyyy HH:mm", "dd/MM/yyyy HH:mm", "dd-MM-yyyy HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDateTime))
        {
            return fallback;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified),
            TimeSpan.FromHours(7));
    }

    [GeneratedRegex(
        @"^(?<name>.+?)\s+(?<hits>\d+|[Oo])\s+(?<accuracy>\d+(?:[\.,]\d+)?(?:/0)?|[Oo])\s*(?:%|/0)?\s+(?<movement>\d+(?:[\.,]\d+)?|[Oo])\s*m\s+(?<score>\d+|[Oo])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerLinePattern();

    [GeneratedRegex(
        @"^(?<name>.+?)\s+(?<hits>\d+|[Oo])\s+(?<shield>\d+|[Oo])\s+(?<towers>\d+|[Oo])\s+(?<score>\d+|[Oo])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MiniBlockTowersPlayerLinePattern();

    [GeneratedRegex(
        @"^(?<name>.+?)\s+(?<hits>\d+|[Oo])\s+(?<shield>\d+|[Oo])\s+(?<towers>\d+|[Oo])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MiniBlockTowersZeroScorePlayerLinePattern();

    [GeneratedRegex(
        @"^(?:w|v|♛|♚|♕|♔)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingCrownArtifactPattern();

    [GeneratedRegex(
        @"^Player\s*(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefaultPlayerNamePattern();

    [GeneratedRegex(
        @"Session\s+(?<session>\d+)\s+Results",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SessionPattern();

    [GeneratedRegex(
        @"(?<date>\d{2}[\./-]\d{2}[\./-]\d{4})\s*[-–]\s*(?<time>\d{2}:\d{2})",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    private sealed record OcrPass(string Name, OcrResult Result, string Text);

    private sealed class SpatialWordRow
    {
        internal List<OcrWord> Words { get; } = [];

        internal double CenterY =>
            Words.Count == 0
                ? 0
                : Words.Average(word => word.BoundingRect.Y + word.BoundingRect.Height / 2d);

        internal double AverageHeight =>
            Words.Count == 0
                ? 0
                : Words.Average(word => word.BoundingRect.Height);
    }
}
