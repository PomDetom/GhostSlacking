using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class ChatTextWrapperTests
{
    [Fact]
    public void Oversized_english_word_breaks_without_losing_characters()
    {
        const string message = "ABCDEFGHIJKL";

        var wrapped = ChatTextWrapper.BreakOversizedWords(message, 50, value => value.Length * 10);

        Assert.Equal("ABCDE\nFGHIJ\nKL", wrapped);
        Assert.Equal(message, wrapped.Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Normal_words_chinese_emoji_and_explicit_line_breaks_are_preserved()
    {
        const string message = "中文 hello 👨‍👩‍👧‍👦\nsecond line";

        var wrapped = ChatTextWrapper.BreakOversizedWords(message, 100, value => value.Length * 5);

        Assert.Equal(message, wrapped);
    }

    [Fact]
    public void Long_latin_word_splits_at_text_element_boundaries()
    {
        const string message = "cafe\u0301cafe\u0301";

        var wrapped = ChatTextWrapper.BreakOversizedWords(message, 5, value =>
            System.Globalization.StringInfo.ParseCombiningCharacters(value).Length);

        Assert.Equal(message, wrapped.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("e\n\u0301", wrapped, StringComparison.Ordinal);
    }
}
