using Tagster.Core;

namespace Tagster.Tests;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("  Док  ", "док")]
    [InlineData("ВойнА", "война")]
    [InlineData("Portrait", "portrait")]
    [InlineData("Чёрно-Белое", "чёрно-белое")]
    [InlineData("two   words", "two words")]
    public void Normalize_trims_collapses_and_lowercases(string input, string expected)
        => Assert.Equal(expected, TagNormalizer.Normalize(input));

    [Fact]
    public void Normalize_blank_is_empty()
        => Assert.Equal("", TagNormalizer.Normalize("   "));

    [Fact]
    public void NormalizeDisplayList_dedupes_by_normalized_form_keeping_first_spelling()
    {
        var result = TagNormalizer.NormalizeDisplayList(["Док", "  ", "док", "Война", "ВОЙНА"]);

        // Two distinct tags; display spelling is the first one seen; ordered by ordinal of the
        // normalized form ("война" precedes "док").
        Assert.Equal(new[] { "Война", "Док" }, result);
    }
}
