using System.Globalization;
using System.Text;

namespace GhostSlacking.App;

internal static class ChatTextWrapper
{
    public static string BreakOversizedWords(string message, double maximumWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(measure);
        if (maximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        var result = new StringBuilder(message.Length);
        var word = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(message);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (IsLatinWordElement(element))
            {
                word.Append(element);
                continue;
            }

            AppendWord(result, word, maximumWidth, measure);
            result.Append(element);
        }

        AppendWord(result, word, maximumWidth, measure);
        return result.ToString();
    }

    private static void AppendWord(
        StringBuilder result,
        StringBuilder word,
        double maximumWidth,
        Func<string, double> measure)
    {
        if (word.Length == 0)
        {
            return;
        }

        var value = word.ToString();
        word.Clear();
        if (measure(value) <= maximumWidth)
        {
            result.Append(value);
            return;
        }

        var boundaries = new List<int> { 0 };
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            if (enumerator.ElementIndex > 0)
            {
                boundaries.Add(enumerator.ElementIndex);
            }
        }

        boundaries.Add(value.Length);
        var start = 0;
        while (start < boundaries.Count - 1)
        {
            var low = start + 1;
            var high = boundaries.Count - 1;
            var lastFit = start;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var candidate = value[boundaries[start]..boundaries[middle]];
                if (measure(candidate) <= maximumWidth)
                {
                    lastFit = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var end = Math.Max(lastFit, start + 1);
            result.Append(value[boundaries[start]..boundaries[end]]);
            if (end < boundaries.Count - 1)
            {
                result.Append('\n');
            }

            start = end;
        }
    }

    private static bool IsLatinWordElement(string value) =>
        ((value[0] <= '\u024F' && char.IsLetterOrDigit(value[0])) || value[0] is '\'' or '’');
}
