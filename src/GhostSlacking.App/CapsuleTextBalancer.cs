using System.Globalization;

namespace GhostSlacking.App;

internal sealed record CapsuleTextLayout(string Text, IReadOnlyList<string> Lines, double TextWidth);

internal static class CapsuleTextBalancer
{
    private const double MeasurementTolerance = 0.5;

    public static CapsuleTextLayout Arrange(string message, double maximumWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(measure);
        if (maximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        var lines = new List<string>();
        foreach (var paragraph in message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            lines.AddRange(BalanceParagraph(paragraph, maximumWidth, measure));
        }

        return new CapsuleTextLayout(
            string.Join("\n", lines),
            lines,
            lines.Count == 0 ? 0 : lines.Max(measure));
    }

    private static IReadOnlyList<string> BalanceParagraph(
        string paragraph,
        double maximumWidth,
        Func<string, double> measure)
    {
        if (paragraph.Length == 0 || measure(paragraph) <= maximumWidth + MeasurementTolerance)
        {
            return [paragraph];
        }

        var elements = GetTextElements(paragraph);
        var breaks = new List<int> { 0 };
        for (var index = 1; index < elements.Count; index++)
        {
            if (CanBreak(elements, index, paragraph, maximumWidth, measure))
            {
                breaks.Add(elements[index].Start);
            }
        }

        breaks.Add(paragraph.Length);
        var count = breaks.Count;
        var widths = new double[count, count];
        for (var start = 0; start < count; start++)
        {
            for (var end = start + 1; end < count; end++)
            {
                var line = paragraph[breaks[start]..breaks[end]].Trim();
                widths[start, end] = line.Length == 0 ? double.PositiveInfinity : measure(line);
            }
        }

        var minimumLines = new int[count];
        Array.Fill(minimumLines, int.MaxValue);
        minimumLines[^1] = 0;
        for (var start = count - 2; start >= 0; start--)
        {
            for (var end = start + 1; end < count; end++)
            {
                if (widths[start, end] <= maximumWidth + MeasurementTolerance && minimumLines[end] < int.MaxValue)
                {
                    minimumLines[start] = Math.Min(minimumLines[start], minimumLines[end] + 1);
                }
            }
        }

        if (minimumLines[0] == int.MaxValue)
        {
            return [paragraph];
        }

        var lineCount = minimumLines[0];
        var minimax = new double[lineCount + 1, count];
        for (var line = 0; line <= lineCount; line++)
        {
            for (var index = 0; index < count; index++)
            {
                minimax[line, index] = double.PositiveInfinity;
            }
        }

        minimax[0, count - 1] = 0;
        for (var line = 1; line <= lineCount; line++)
        {
            for (var start = count - 2; start >= 0; start--)
            {
                for (var end = start + 1; end < count; end++)
                {
                    var width = widths[start, end];
                    if (width <= maximumWidth + MeasurementTolerance && double.IsFinite(minimax[line - 1, end]))
                    {
                        minimax[line, start] = Math.Min(
                            minimax[line, start],
                            Math.Max(width, minimax[line - 1, end]));
                    }
                }
            }
        }

        var balancedWidth = minimax[lineCount, 0];
        var costs = new double[lineCount + 1, count];
        var next = new int[lineCount + 1, count];
        for (var line = 0; line <= lineCount; line++)
        {
            for (var index = 0; index < count; index++)
            {
                costs[line, index] = double.PositiveInfinity;
                next[line, index] = -1;
            }
        }

        costs[0, count - 1] = 0;
        for (var line = 1; line <= lineCount; line++)
        {
            for (var start = count - 2; start >= 0; start--)
            {
                for (var end = start + 1; end < count; end++)
                {
                    var width = widths[start, end];
                    if (width > balancedWidth + MeasurementTolerance || !double.IsFinite(costs[line - 1, end]))
                    {
                        continue;
                    }

                    var gap = balancedWidth - width;
                    var cost = costs[line - 1, end] + (gap * gap);
                    if (cost < costs[line, start])
                    {
                        costs[line, start] = cost;
                        next[line, start] = end;
                    }
                }
            }
        }

        var result = new List<string>(lineCount);
        var position = 0;
        for (var remaining = lineCount; remaining > 0; remaining--)
        {
            var end = next[remaining, position];
            if (end < 0)
            {
                return [paragraph];
            }

            result.Add(paragraph[breaks[position]..breaks[end]].Trim());
            position = end;
        }

        return result;
    }

    private static List<(int Start, string Value)> GetTextElements(string text)
    {
        var result = new List<(int Start, string Value)>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            result.Add((enumerator.ElementIndex, enumerator.GetTextElement()));
        }

        return result;
    }

    private static bool CanBreak(
        IReadOnlyList<(int Start, string Value)> elements,
        int index,
        string paragraph,
        double maximumWidth,
        Func<string, double> measure)
    {
        var left = elements[index - 1].Value;
        var right = elements[index].Value;
        if (IsLatinWordElement(left) && IsLatinWordElement(right))
        {
            var start = index - 1;
            while (start > 0 && IsLatinWordElement(elements[start - 1].Value))
            {
                start--;
            }

            var end = index;
            while (end + 1 < elements.Count && IsLatinWordElement(elements[end + 1].Value))
            {
                end++;
            }

            var word = paragraph[elements[start].Start..(end + 1 < elements.Count ? elements[end + 1].Start : paragraph.Length)];
            return measure(word) > maximumWidth + MeasurementTolerance;
        }

        return !"，。！？：；、）》」』】,.!?;:)]}".Contains(right, StringComparison.Ordinal) &&
            !"（《「『【([{".Contains(left, StringComparison.Ordinal);
    }

    private static bool IsLatinWordElement(string value) =>
        value.Length == 1 &&
        ((value[0] <= '\u024F' && char.IsLetterOrDigit(value[0])) || value[0] is '\'' or '’');
}
