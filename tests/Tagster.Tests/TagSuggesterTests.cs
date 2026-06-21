using Tagster.Core;

namespace Tagster.Tests;

public class TagSuggesterTests
{
    // Stored best-first (by usage), the order the autocomplete pool should preserve.
    private static readonly string[] Archive = ["док", "портрет", "война", "Чёрно-белое"];

    [Fact]
    public void Available_excludes_already_applied_case_insensitively()
    {
        var pool = TagSuggester.Available(Archive, ["ДОК"]);
        Assert.Equal(new[] { "портрет", "война", "Чёрно-белое" }, pool);
    }

    [Fact]
    public void Available_preserves_input_order_when_nothing_applied()
    {
        Assert.Equal(Archive, TagSuggester.Available(Archive, []));
    }

    [Fact]
    public void ExactMatch_resolves_a_known_tag_to_its_canonical_spelling()
    {
        // Typed with different case and surrounding space — still a known tag, reused as stored.
        Assert.Equal("портрет", TagSuggester.ExactMatch(Archive, "  Портрет "));
    }

    [Fact]
    public void ExactMatch_is_null_for_a_typo_or_empty_text()
    {
        Assert.Null(TagSuggester.ExactMatch(Archive, "портрте")); // a typo → would be a new tag
        Assert.Null(TagSuggester.ExactMatch(Archive, "   "));
    }
}
