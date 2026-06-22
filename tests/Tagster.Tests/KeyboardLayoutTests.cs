using Tagster.Core;

namespace Tagster.Tests;

public class KeyboardLayoutTests
{
    [Fact]
    public void Swap_maps_a_wrong_layout_query_to_what_was_meant()
    {
        // English layout left active while typing the Russian word «лес».
        Assert.Equal("лес", KeyboardLayout.Swap("ktc"));
        // Russian layout left active while typing "smith".
        Assert.Equal("smith", KeyboardLayout.Swap("ыьшер"));
    }

    [Fact]
    public void Swap_covers_punctuation_keys_that_carry_a_cyrillic_letter()
    {
        // б and ъ live on punctuation keys: «объект» typed with English active is "j,]trn".
        Assert.Equal("объект", KeyboardLayout.Swap("j,]trn"));
    }

    [Fact]
    public void Swap_round_trips_because_the_map_is_a_bijection()
    {
        foreach (var text in new[] { "ktc", "лес", "j,]tnr", "объект", "hello", "привет" })
            Assert.Equal(text, KeyboardLayout.Swap(KeyboardLayout.Swap(text)));
    }

    [Fact]
    public void Swap_passes_through_characters_not_on_a_swappable_key()
    {
        Assert.Equal("2024 ", KeyboardLayout.Swap("2024 ")); // digits and space are layout-neutral
        Assert.Equal("", KeyboardLayout.Swap(null));
    }

    [Fact]
    public void MatchesEitherLayout_finds_a_tag_typed_in_the_wrong_layout()
    {
        Assert.True(KeyboardLayout.MatchesEitherLayout("лес", "ktc"));   // meant Cyrillic, typed Latin
        Assert.True(KeyboardLayout.MatchesEitherLayout("smith", "ыьшер")); // meant Latin, typed Cyrillic
    }

    [Fact]
    public void MatchesEitherLayout_still_matches_a_plain_substring()
    {
        Assert.True(KeyboardLayout.MatchesEitherLayout("портрет", "трет")); // ordinary correct-layout typing
        Assert.True(KeyboardLayout.MatchesEitherLayout("anything", ""));    // empty query matches everything
    }

    [Fact]
    public void MatchesEitherLayout_rejects_text_that_matches_under_neither_layout()
    {
        Assert.False(KeyboardLayout.MatchesEitherLayout("лес", "zzz"));
    }
}
