
namespace GhostSlacking.App;

internal enum ReleaseNotesBlockKind
{
    Heading,
    Bullet,
    Paragraph
}

internal sealed record ReleaseNotesBlock(ReleaseNotesBlockKind Kind, string Text);

internal static class ReleaseNotesFormatter
{
    public static IReadOnlyList<ReleaseNotesBlock> Parse(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return [];
        }

        var blocks = new List<ReleaseNotesBlock>();
        var paragraph = new List<string>();
        foreach (var rawLine in notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddIfNotEmpty(ReleaseNotesBlockKind.Heading, line[4..]);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddIfNotEmpty(ReleaseNotesBlockKind.Bullet, line[2..]);
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph();
        return blocks;

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            AddIfNotEmpty(ReleaseNotesBlockKind.Paragraph, string.Join(' ', paragraph));
            paragraph.Clear();
        }

        void AddIfNotEmpty(ReleaseNotesBlockKind kind, string value)
        {
            value = value.Trim();
            if (value.Length > 0)
            {
                blocks.Add(new ReleaseNotesBlock(kind, value));
            }
        }
    }
}
