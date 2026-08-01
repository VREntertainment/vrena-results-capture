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

    private static readonly GameDefinition LaserTag =
        new("Laser Tag", "laser-tag", GameLayout.StandardShooter);
    private static readonly GameDefinition MiniBlockTowers =
        new("Mini Block Towers", "mini-block-towers", GameLayout.MiniBlockTowers);
    private static readonly GameDefinition OfficeWar =
        new("Office War", "office-war", GameLayout.StandardShooter);
    private static readonly GameDefinition Paintball =
        new("Paintball", "paintball", GameLayout.StandardShooter);
    private static readonly GameDefinition SnowBattle =
        new("Snow Battle", "snow-battle", GameLayout.StandardShooter);
    private static readonly GameDefinition CastleUnspunnen =
        new("Castle Unspunnen", "castle-unspunnen", GameLayout.StandardShooter);
    private static readonly GameDefinition WildWest =
        new("Wild West", "wild-west", GameLayout.StandardShooter);
    private static readonly GameDefinition SecretArc =
        new("The Secret of the Arc", "arc-of-the-covenant", GameLayout.Escape);
    private static readonly GameDefinition JollerHouse =
        new("Joller House", "joller-house", GameLayout.Escape);
    private static readonly GameDefinition ZgMarbles =
        new("ZG Marbles", "zg-marbles", GameLayout.Goals);

    private static readonly IReadOnlyDictionary<string, GameDefinition> Games =
        new Dictionary<string, GameDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["lasertag"] = LaserTag,
            ["mbtowers"] = MiniBlockTowers,
            ["miniblocktowers"] = MiniBlockTowers,
            ["officewar"] = OfficeWar,
            ["paintball"] = Paintball,
            ["showbattle"] = SnowBattle,
            ["snowbattle"] = SnowBattle,
            ["unspunnen"] = CastleUnspunnen,
            ["castleunspunnen"] = CastleUnspunnen,
            ["wildwest"] = WildWest,
            ["arcofthecovenant"] = SecretArc,
            ["dgb"] = SecretArc,
            ["secretarc"] = SecretArc,
            ["joller"] = JollerHouse,
            ["jollerhouse"] = JollerHouse,
            ["zgmarbles"] = ZgMarbles
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
            await RecognizeRegionAsync(engine, screenshot, "game-footer", new RectangleF(0, 0.90f, 0.25f, 0.10f)),
            await RecognizeRegionAsync(engine, screenshot, "game", new RectangleF(0, 0.72f, 0.48f, 0.28f))
        };

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
            var unidentifiedDiagnosticText = BuildDiagnosticText(passes);
            DiagnosticLog.SaveOcrText(captureId, unidentifiedDiagnosticText);
            DiagnosticLog.Warning($"OCR did not identify a supported game. CaptureId={captureId}");
            return new ResultReadOutcome(
                captureId,
                unidentifiedDiagnosticText,
                null,
                "game_not_recognized");
        }

        if (game.Layout is GameLayout.Escape)
        {
            passes.Add(await RecognizeRegionAsync(
                engine,
                screenshot,
                "escape-time",
                new RectangleF(0.25f, 0.26f, 0.50f, 0.42f)));
            passes.Add(await RecognizeRegionAsync(
                engine,
                screenshot,
                "left-escape-players",
                new RectangleF(0, 0.405f, 0.27f, 0.09f),
                OcrPreparation.FaintText));
            passes.Add(await RecognizeRegionAsync(
                engine,
                screenshot,
                "right-escape-players",
                new RectangleF(0.73f, 0.405f, 0.27f, 0.09f),
                OcrPreparation.FaintText));
        }
        else
        {
            // A single team crop contains both vertically stacked player rows. Keeping
            // the teams separate prevents spatial OCR from merging left and right names.
            passes.Add(await RecognizeRegionAsync(
                engine,
                screenshot,
                "left-players",
                new RectangleF(0.02f, 0.25f, 0.47f, 0.24f)));
            passes.Add(await RecognizeRegionAsync(
                engine,
                screenshot,
                "right-players",
                new RectangleF(0.51f, 0.25f, 0.47f, 0.24f)));
        }

        var diagnosticText = BuildDiagnosticText(passes);
        DiagnosticLog.SaveOcrText(captureId, diagnosticText);
        var fullText = string.Join(
            Environment.NewLine,
            passes.Select(pass => pass.Text));

        int? escapeDurationSeconds = null;
        if (game.Layout is GameLayout.Escape)
        {
            escapeDurationSeconds = ParseEscapeDurationSeconds(fullText);
            if (escapeDurationSeconds is null)
            {
                DiagnosticLog.Warning($"OCR did not identify the escape time. CaptureId={captureId}");
                return new ResultReadOutcome(
                    captureId,
                    diagnosticText,
                    null,
                    "escape_time_not_recognized");
            }
        }

        var extraction = ExtractPlayers(passes, game, escapeDurationSeconds);
        var players = extraction.Players;

        if (extraction.HasConflictingRows || extraction.HasIncompleteRows)
        {
            DiagnosticLog.Warning(
                $"OCR produced incomplete or conflicting player rows. CaptureId={captureId}; " +
                $"Incomplete={extraction.HasIncompleteRows}; Conflicting={extraction.HasConflictingRows}");
            return new ResultReadOutcome(
                captureId,
                diagnosticText,
                null,
                extraction.HasConflictingRows
                    ? "player_rows_conflict"
                    : "player_rows_incomplete");
        }

        if (players.Count == 0)
        {
            DiagnosticLog.Warning($"OCR did not identify any complete player row. CaptureId={captureId}");
            return new ResultReadOutcome(
                captureId,
                diagnosticText,
                null,
                "players_not_recognized");
        }

        if (players.Count > 4)
        {
            DiagnosticLog.Warning(
                $"OCR identified more than four player rows. CaptureId={captureId}; Players={players.Count}");
            return new ResultReadOutcome(
                captureId,
                diagnosticText,
                null,
                "player_count_invalid");
        }

        DiagnosticLog.Info(
            $"OCR recognized result. CaptureId={captureId}; Game={game.Slug}; " +
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
                GameName = game.Name,
                GameSlug = game.Slug,
                Players = players
            },
            null);
    }

    private static async Task<OcrPass> RecognizeRegionAsync(
        OcrEngine engine,
        Bitmap screenshot,
        string name,
        RectangleF fractionalRegion,
        OcrPreparation preparation = OcrPreparation.Default)
    {
        using var prepared = PrepareRegion(screenshot, fractionalRegion, preparation);
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

    private static Bitmap PrepareRegion(
        Bitmap screenshot,
        RectangleF fractionalRegion,
        OcrPreparation preparation)
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
        var destination = new Rectangle(0, 0, targetWidth, targetHeight);
        var source = new Rectangle(x, y, width, height);
        if (preparation is OcrPreparation.FaintText)
        {
            using var attributes = FaintTextImageAttributes();
            graphics.DrawImage(
                screenshot,
                destination,
                source.X,
                source.Y,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }
        else
        {
            graphics.DrawImage(screenshot, destination, source, GraphicsUnit.Pixel);
        }

        return prepared;
    }

    private static ImageAttributes FaintTextImageAttributes()
    {
        // Escape screens render names as very dark text over dark team strips. A high
        // gamma lifts those shadow details; grayscale and modest contrast make the
        // result legible to Windows OCR without altering the saved source screenshot.
        var attributes = new ImageAttributes();
        attributes.SetGamma(5f, ColorAdjustType.Bitmap);
        attributes.SetColorMatrix(
            new ColorMatrix(
            [
                [0.4485f, 0.4485f, 0.4485f, 0, 0],
                [0.8805f, 0.8805f, 0.8805f, 0, 0],
                [0.1710f, 0.1710f, 0.1710f, 0, 0],
                [0, 0, 0, 1, 0],
                [0.10f, 0.10f, 0.10f, 0, 1]
            ]),
            ColorMatrixFlag.Default,
            ColorAdjustType.Bitmap);
        return attributes;
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

    private static PlayerExtraction ExtractPlayers(
        IEnumerable<OcrPass> passes,
        GameDefinition game,
        int? escapeDurationSeconds)
    {
        var playerPasses = game.Layout is GameLayout.Escape
            ? passes.Where(pass => pass.Name.Contains("escape-players", StringComparison.Ordinal))
            : passes.Where(pass => pass.Name is "left-players" or "right-players");

        var candidates = new List<RecognizedPlayer>();
        var hasIncompleteRows = false;
        foreach (var row in playerPasses.SelectMany(pass => CandidateRows(pass.Result)))
        {
            var player = game.Layout is GameLayout.Escape
                ? ParseEscapePlayerLine(row, escapeDurationSeconds!.Value)
                : ParsePlayerLine(row, game);
            if (player is not null)
            {
                candidates.Add(player);
            }
            else if (game.Layout is not GameLayout.Escape && LooksLikeIncompletePlayerRow(row))
            {
                hasIncompleteRows = true;
            }
        }

        var merged = MergeDuplicatePlayers(candidates);
        return merged with { HasIncompleteRows = hasIncompleteRows };
    }

    private static bool LooksLikeIncompletePlayerRow(string source)
    {
        var line = Regex.Replace(source, @"\s+", " ").Trim();
        if (Regex.IsMatch(
                line,
                @"\b(?:team|hits|accuracy|acc|movement|mov|goals|shield|towers|total)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(line, @"\p{L}", RegexOptions.CultureInvariant) &&
            Regex.Matches(line, @"\d+(?:[\.,]\d+)?", RegexOptions.CultureInvariant).Count >= 2;
    }

    private static PlayerExtraction MergeDuplicatePlayers(
        IEnumerable<RecognizedPlayer> candidates)
    {
        var players = new List<RecognizedPlayer>();
        var hasConflictingRows = false;
        foreach (var candidate in candidates)
        {
            var candidateIdentity = NormalizedPlayerIdentity(candidate.Name);
            var identicalNameIndex = players.FindIndex(existing =>
                candidateIdentity.Equals(
                    NormalizedPlayerIdentity(existing.Name),
                    StringComparison.Ordinal));
            if (identicalNameIndex >= 0)
            {
                if (!SameStatistics(players[identicalNameIndex], candidate))
                {
                    hasConflictingRows = true;
                }

                continue;
            }

            var duplicateIndex = players.FindIndex(existing =>
            {
                var existingIdentity = NormalizedPlayerIdentity(existing.Name);
                return SameStatistics(existing, candidate) &&
                    Math.Min(candidateIdentity.Length, existingIdentity.Length) >= 5 &&
                    (candidateIdentity.EndsWith(existingIdentity, StringComparison.Ordinal) ||
                     existingIdentity.EndsWith(candidateIdentity, StringComparison.Ordinal));
            });

            if (duplicateIndex < 0)
            {
                players.Add(candidate);
                continue;
            }

            if (PlayerNameQuality(candidate.Name) > PlayerNameQuality(players[duplicateIndex].Name))
            {
                players[duplicateIndex] = candidate;
            }
        }

        return new PlayerExtraction(players, hasConflictingRows, false);
    }

    private static string NormalizedPlayerIdentity(string name) =>
        Regex.Replace(name.Normalize(NormalizationForm.FormD), @"[^\p{L}\p{N}]", string.Empty)
            .ToLowerInvariant();

    private static bool SameStatistics(RecognizedPlayer first, RecognizedPlayer second) =>
        first.Hits == second.Hits &&
        first.AccuracyPercent == second.AccuracyPercent &&
        first.MovementMeters == second.MovementMeters &&
        first.Score == second.Score;

    private static int PlayerNameQuality(string name)
    {
        var normalized = Regex.Replace(name, @"\s+", string.Empty);
        var quality = normalized.Count(char.IsLetterOrDigit);
        if (Regex.IsMatch(normalized, @"^Player[1-4]$", RegexOptions.IgnoreCase))
        {
            quality += 50;
        }

        if (Regex.IsMatch(name, @"^(?:no|total|team)\b", RegexOptions.IgnoreCase))
        {
            quality -= 30;
        }

        return quality;
    }

    private static RecognizedPlayer? ParsePlayerLine(string source, GameDefinition game)
    {
        var line = Regex.Replace(source, @"\s+", " ").Trim();
        if (game.Layout is GameLayout.MiniBlockTowers)
        {
            var miniBlockTowersPlayer = ParseMiniBlockTowersPlayerLine(line);
            if (miniBlockTowersPlayer is not null)
            {
                return miniBlockTowersPlayer;
            }
        }

        if (game.Layout is GameLayout.Goals)
        {
            return ParseGoalsPlayerLine(line, game.Slug);
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

        if (!OcrResultValueParser.TryInteger(match.Groups["hits"].Value, out var hits) ||
            !OcrResultValueParser.TryScore(match.Groups["score"].Value, game.Slug, out var score) ||
            !OcrResultValueParser.TryAccuracy(match.Groups["accuracy"].Value, out var accuracy) ||
            !OcrResultValueParser.TryDecimal(match.Groups["movement"].Value, out var movement))
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

    private static RecognizedPlayer? ParseGoalsPlayerLine(string line, string gameSlug)
    {
        var match = GoalsPlayerLinePattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var name = CleanPlayerName(match.Groups["name"].Value);
        if (name is null ||
            !OcrResultValueParser.TryInteger(match.Groups["hits"].Value, out var hits) ||
            !OcrResultValueParser.TryAccuracy(match.Groups["accuracy"].Value, out var accuracy) ||
            !OcrResultValueParser.TryInteger(match.Groups["goals"].Value, out var goals) ||
            !OcrResultValueParser.TryScore(match.Groups["score"].Value, gameSlug, out var score) ||
            hits < 0 || accuracy is < 0 or > 100 || goals < 0 || score < 0)
        {
            return null;
        }

        return new RecognizedPlayer
        {
            Name = name,
            Hits = hits,
            AccuracyPercent = accuracy,
            MovementMeters = null,
            Score = score
        };
    }

    private static RecognizedPlayer? ParseEscapePlayerLine(
        string source,
        int escapeDurationSeconds)
    {
        var line = Regex.Replace(source, @"\s+", " ").Trim();
        var match = EscapePlayerScoreFirstPattern().Match(line);
        if (!match.Success)
        {
            match = EscapePlayerScoreLastPattern().Match(line);
        }

        if (match.Success &&
            (!OcrResultValueParser.TryInteger(match.Groups["teamScore"].Value, out var teamScore) ||
             teamScore < 0))
        {
            return null;
        }

        if (!match.Success)
        {
            match = EscapePlayerNameOnlyPattern().Match(line);
        }

        if (!match.Success)
        {
            return null;
        }

        var name = CleanPlayerName(match.Groups["name"].Value);
        return name is null || name.Count(char.IsLetter) < 2
            ? null
            : new RecognizedPlayer
            {
                Name = name,
                Hits = 0,
                AccuracyPercent = null,
                MovementMeters = null,
                Score = escapeDurationSeconds
            };
    }

    private static RecognizedPlayer? ParseMiniBlockTowersPlayerLine(string line)
    {
        var match = MiniBlockTowersPlayerLinePattern().Match(line);
        if (!match.Success)
        {
            var zeroScoreMatch = MiniBlockTowersZeroScorePlayerLinePattern().Match(line);
            if (!zeroScoreMatch.Success ||
                !OcrResultValueParser.TryInteger(zeroScoreMatch.Groups["hits"].Value, out var zeroHits) ||
                !OcrResultValueParser.TryInteger(zeroScoreMatch.Groups["shield"].Value, out var zeroShield) ||
                !OcrResultValueParser.TryInteger(zeroScoreMatch.Groups["towers"].Value, out var zeroTowers) ||
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
            !OcrResultValueParser.TryInteger(match.Groups["hits"].Value, out var hits) ||
            !OcrResultValueParser.TryInteger(match.Groups["shield"].Value, out var shield) ||
            !OcrResultValueParser.TryInteger(match.Groups["towers"].Value, out var towers) ||
            !OcrResultValueParser.TryInteger(match.Groups["score"].Value, out var score) ||
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
        var name = value.Trim(' ', '"', '“', '”', ':', ';', '|', '·', '•');
        name = LeadingCrownArtifactPattern().Replace(name, string.Empty).Trim();
        name = Regex.Replace(
            name,
            @"^no\s+(?=Player\s*[1-4]\b)",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        name = DefaultPlayerNamePattern().Replace(name, "Player$1");
        if (name.Length is < 1 or > 80 ||
            Regex.IsMatch(name, @"^(?:team|escape\s*time|not\s+all)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(name, @"\b(?:hits|acc|mov|goals|shield|towers|total)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(name, @"\b\d+(?:[\.,]\d+)?\s*m\b", RegexOptions.IgnoreCase) ||
            Regex.Matches(name, @"(?:^|\s)(?:\d+(?:[\.,]\d+)?|[Oo])(?=\s|$)").Count > 1)
        {
            return null;
        }

        return name;
    }

    private static GameDefinition? FindGame(string text)
    {
        var normalized = Regex.Replace(text, @"[^A-Za-z0-9]", string.Empty);
        var tokens = Regex.Matches(text, @"[A-Za-z0-9]+")
            .Select(match => match.Value)
            .ToList();
        foreach (var game in Games.OrderByDescending(item => item.Key.Length))
        {
            var exactMatch = game.Key.Length < 5
                ? tokens.Contains(game.Key, StringComparer.OrdinalIgnoreCase)
                : normalized.Contains(game.Key, StringComparison.OrdinalIgnoreCase);
            if (exactMatch)
            {
                return game.Value;
            }
        }

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
            if (game.Key.Length < 5)
            {
                continue;
            }

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

    private static int? ParseEscapeDurationSeconds(string text)
    {
        var match = EscapeTimePattern().Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups["minutes"].Value, out var minutes) ||
            !int.TryParse(match.Groups["seconds"].Value, out var seconds) ||
            minutes < 0 || seconds is < 0 or > 59)
        {
            return null;
        }

        return checked(minutes * 60 + seconds);
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
        @"^(?<name>.+?)\s+(?<hits>\d+|[Oo])\s+(?<accuracy>\d+(?:[\.,]\d+)?(?:/0)?|[Oo])\s*(?:%|/0)?\s+(?<goals>\d+|[Oo])\s+(?<score>\d+|[Oo])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoalsPlayerLinePattern();

    [GeneratedRegex(
        @"^[^\p{L}\p{N}]*(?<teamScore>\d+|[Oo])\s+(?<name>[\p{L}\p{M}][\p{L}\p{M}\p{N}'’._ -]{0,79}?)[^\p{L}\p{N}]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapePlayerScoreFirstPattern();

    [GeneratedRegex(
        @"^[^\p{L}\p{N}]*(?<name>[\p{L}\p{M}][\p{L}\p{M}\p{N}'’._ -]{0,79}?)\s+(?<teamScore>\d+|[Oo])[^\p{L}\p{N}]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapePlayerScoreLastPattern();

    [GeneratedRegex(
        @"^[^\p{L}\p{N}]*(?<name>[\p{L}\p{M}][\p{L}\p{M}'’._ -]{1,79})[^\p{L}\p{N}]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapePlayerNameOnlyPattern();

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

    [GeneratedRegex(
        @"Escape\s*Time\s*(?<minutes>\d+):(?<seconds>\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapeTimePattern();

    private sealed record OcrPass(string Name, OcrResult Result, string Text);

    private sealed record PlayerExtraction(
        List<RecognizedPlayer> Players,
        bool HasConflictingRows,
        bool HasIncompleteRows);

    private sealed record GameDefinition(string Name, string Slug, GameLayout Layout);

    private enum GameLayout
    {
        StandardShooter,
        MiniBlockTowers,
        Goals,
        Escape
    }

    private enum OcrPreparation
    {
        Default,
        FaintText
    }

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
