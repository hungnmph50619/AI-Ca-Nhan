using System.Globalization;
using System.Text;

namespace PersonalAI.Web.Services;

public enum DesktopOcrResolutionStatus
{
    Resolved,
    NotFound,
    Ambiguous
}

public sealed record DesktopOcrTarget(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    int Score,
    string Source);

public sealed record DesktopOcrResolution(
    DesktopOcrResolutionStatus Status,
    DesktopOcrTarget? Target,
    IReadOnlyList<DesktopOcrTarget> Candidates,
    string Reason)
{
    public bool Resolved =>
        Status == DesktopOcrResolutionStatus.Resolved &&
        Target is not null;
}

public sealed class DesktopOcrTargetResolver
{
    public DesktopOcrResolution Resolve(
        DesktopOcrObservation observation,
        string requestedText)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var target =
            Normalize(
                requestedText);

        if (!observation.Available ||
            target.Length == 0)
        {
            return new(
                DesktopOcrResolutionStatus.NotFound,
                null,
                Array.Empty<DesktopOcrTarget>(),
                observation.Available
                    ? "OCR target rỗng."
                    : $"OCR provider không khả dụng: {observation.Reason}");
        }

        var candidates =
            new Dictionary<string, DesktopOcrTarget>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var line in observation.Lines)
        {
            AddLinePhraseCandidates(
                line,
                target,
                candidates);

            foreach (var word in line.Words)
            {
                if (word.Width <= 1 ||
                    word.Height <= 1)
                {
                    continue;
                }

                var normalizedWord =
                    Normalize(
                        word.Text);

                if (normalizedWord.Length == 0)
                    continue;

                var score =
                    normalizedWord.Equals(
                        target,
                        StringComparison.Ordinal)
                        ? 110
                        : normalizedWord.Contains(
                            target,
                            StringComparison.Ordinal) &&
                          target.Length >= 3
                            ? 90
                            : -1;

                if (score < 0)
                    continue;

                AddCandidate(
                    candidates,
                    new(
                        word.Text,
                        word.Left,
                        word.Top,
                        word.Width,
                        word.Height,
                        score,
                        "word"));
            }
        }

        var ordered =
            candidates.Values
                .OrderByDescending(item =>
                    item.Score)
                .ThenBy(item =>
                    item.Top)
                .ThenBy(item =>
                    item.Left)
                .Take(8)
                .ToArray();

        if (ordered.Length == 0)
        {
            return new(
                DesktopOcrResolutionStatus.NotFound,
                null,
                ordered,
                $"Windows OCR không tìm thấy text '{requestedText}'.");
        }

        var topScore =
            ordered[0].Score;

        var tied =
            ordered
                .Where(item =>
                    item.Score == topScore)
                .ToArray();

        if (tied.Length != 1)
        {
            return new(
                DesktopOcrResolutionStatus.Ambiguous,
                null,
                ordered,
                $"OCR có {tied.Length} target cùng score={topScore}; từ chối đoán.");
        }

        return new(
            DesktopOcrResolutionStatus.Resolved,
            ordered[0],
            ordered,
            $"OCR resolver chọn duy nhất '{ordered[0].Text}' score={ordered[0].Score} từ {ordered[0].Source}.");
    }

    private static void AddLinePhraseCandidates(
        DesktopOcrLine line,
        string target,
        IDictionary<string, DesktopOcrTarget> candidates)
    {
        var words =
            line.Words
                .Where(word =>
                    word.Width > 1 &&
                    word.Height > 1)
                .ToArray();

        if (words.Length == 0)
            return;

        var targetTokens =
            target.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (targetTokens.Length == 0)
            return;

        var normalizedWords =
            words
                .Select(word =>
                    Normalize(
                        word.Text))
                .ToArray();

        for (var start = 0;
             start < normalizedWords.Length;
             start++)
        {
            if (!normalizedWords[start].Equals(
                    targetTokens[0],
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (start + targetTokens.Length >
                normalizedWords.Length)
            {
                continue;
            }

            var matches = true;
            for (var offset = 0;
                 offset < targetTokens.Length;
                 offset++)
            {
                if (!normalizedWords[start + offset].Equals(
                        targetTokens[offset],
                        StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
                continue;

            var matched =
                words
                    .Skip(start)
                    .Take(targetTokens.Length)
                    .ToArray();

            var left =
                matched.Min(word =>
                    word.Left);
            var top =
                matched.Min(word =>
                    word.Top);
            var right =
                matched.Max(word =>
                    word.Left +
                    word.Width);
            var bottom =
                matched.Max(word =>
                    word.Top +
                    word.Height);

            AddCandidate(
                candidates,
                new(
                    string.Join(
                        " ",
                        matched.Select(word =>
                            word.Text)),
                    left,
                    top,
                    right - left,
                    bottom - top,
                    targetTokens.Length > 1
                        ? 120
                        : 110,
                    targetTokens.Length > 1
                        ? "phrase"
                        : "word"));
        }

        var normalizedLine =
            Normalize(
                line.Text);

        if (normalizedLine.Equals(
                target,
                StringComparison.Ordinal))
        {
            var left =
                words.Min(word =>
                    word.Left);
            var top =
                words.Min(word =>
                    word.Top);
            var right =
                words.Max(word =>
                    word.Left +
                    word.Width);
            var bottom =
                words.Max(word =>
                    word.Top +
                    word.Height);

            AddCandidate(
                candidates,
                new(
                    line.Text,
                    left,
                    top,
                    right - left,
                    bottom - top,
                    115,
                    "line"));
        }
    }

    private static void AddCandidate(
        IDictionary<string, DesktopOcrTarget> candidates,
        DesktopOcrTarget candidate)
    {
        var key =
            $"{Math.Round(candidate.Left, 1)}:{Math.Round(candidate.Top, 1)}:{Math.Round(candidate.Width, 1)}:{Math.Round(candidate.Height, 1)}";

        if (!candidates.TryGetValue(
                key,
                out var existing) ||
            candidate.Score >
                existing.Score)
        {
            candidates[key] =
                candidate;
        }
    }

    internal static string Normalize(
        string? value)
    {
        var decomposed =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD);

        var builder =
            new StringBuilder(
                decomposed.Length);

        var previousSpace = true;

        foreach (var ch in decomposed)
        {
            var category =
                CharUnicodeInfo.GetUnicodeCategory(
                    ch);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousSpace = false;
                continue;
            }

            if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }

        return builder
            .ToString()
            .Trim();
    }
}
