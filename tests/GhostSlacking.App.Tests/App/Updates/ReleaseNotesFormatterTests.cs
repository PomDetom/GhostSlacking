using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class ReleaseNotesFormatterTests
{
    [Fact]
    public void Supported_release_notes_are_split_into_safe_display_blocks()
    {
        var blocks = ReleaseNotesFormatter.Parse("""
            ### Features
            - Add a dedicated update window.

            This paragraph
            spans two lines.
            """);

        Assert.Equal(
            [
                new ReleaseNotesBlock(ReleaseNotesBlockKind.Heading, "Features"),
                new ReleaseNotesBlock(ReleaseNotesBlockKind.Bullet, "Add a dedicated update window."),
                new ReleaseNotesBlock(ReleaseNotesBlockKind.Paragraph, "This paragraph spans two lines.")
            ],
            blocks);
    }

    [Fact]
    public void Empty_release_notes_create_no_blocks()
    {
        Assert.Empty(ReleaseNotesFormatter.Parse(null));
        Assert.Empty(ReleaseNotesFormatter.Parse("   "));
    }
}
