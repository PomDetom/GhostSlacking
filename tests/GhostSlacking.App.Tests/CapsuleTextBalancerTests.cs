using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class CapsuleTextBalancerTests
{
    [Fact]
    public void Short_message_remains_on_one_natural_width_line()
    {
        var layout = CapsuleTextBalancer.Arrange("短消息", 10, text => text.Length);

        Assert.Equal(["短消息"], layout.Lines);
        Assert.Equal(3, layout.TextWidth);
    }

    [Fact]
    public void Chinese_message_uses_the_fewest_balanced_lines_and_narrowest_width()
    {
        var layout = CapsuleTextBalancer.Arrange("一二三四五六七八九十", 6, text => text.Length);

        Assert.Equal(["一二三四五", "六七八九十"], layout.Lines);
        Assert.Equal(5, layout.TextWidth);
    }

    [Fact]
    public void Mixed_message_keeps_english_words_intact()
    {
        const string message = "Ready 窗口已隐藏，请按 Alt 查看";
        var layout = CapsuleTextBalancer.Arrange(message, 10, text => text.Length);

        Assert.Contains(layout.Lines, line => line.Contains("Ready", StringComparison.Ordinal));
        Assert.Contains(layout.Lines, line => line.Contains("Alt", StringComparison.Ordinal));
        Assert.Equal(message.Replace(" ", string.Empty, StringComparison.Ordinal),
            string.Concat(layout.Lines).Replace(" ", string.Empty, StringComparison.Ordinal));
        Assert.All(layout.Lines, line => Assert.InRange(line.Length, 1, 10));
    }

    [Fact]
    public void Oversized_english_word_breaks_only_when_necessary()
    {
        var layout = CapsuleTextBalancer.Arrange("LongUnbrokenWord", 6, text => text.Length);

        Assert.Equal(3, layout.Lines.Count);
        Assert.Equal("LongUnbrokenWord", string.Concat(layout.Lines));
        Assert.All(layout.Lines, line => Assert.InRange(line.Length, 1, 6));
    }

    [Fact]
    public void English_contraction_stays_together_when_it_fits()
    {
        var layout = CapsuleTextBalancer.Arrange("Review what's new", 8, text => text.Length);

        Assert.Contains(layout.Lines, line => line.Contains("what's", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_line_breaks_are_preserved()
    {
        var layout = CapsuleTextBalancer.Arrange("第一行\n\n第二行", 10, text => text.Length);

        Assert.Equal(["第一行", "", "第二行"], layout.Lines);
        Assert.Equal("第一行\n\n第二行", layout.Text);
    }
}
