namespace Tagster.Core;

/// <summary>
/// Transposes text between the standard Russian <c>ЙЦУКЕН</c> and US <c>QWERTY</c> keyboard layouts
/// by physical key position. A Russian user who forgets which layout is active types the right keys
/// but gets the wrong script — meaning «лес» but, with English active, producing "ktc". The two
/// layouts share one key map, so a single pass converts either way: <c>Swap("ktc") == "лес"</c> and
/// <c>Swap("лес") == "ktc"</c>. This powers layout-tolerant tag search — see
/// <see cref="MatchesEitherLayout"/>. It is a fixed transposition, not a query of the OS layout.
/// </summary>
public static class KeyboardLayout
{
    // Glyphs at the same physical keys: each QWERTY character (letters plus the punctuation keys
    // that carry a Cyrillic letter) paired with the Russian glyph the other layout produces there.
    private const string Qwerty = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string Russian = "йцукенгшщзхъфывапролджэячсмитьбюё";

    private static readonly Dictionary<char, char> Map = BuildMap();

    /// <summary>
    /// Re-maps each character to the glyph the other layout produces at the same physical key;
    /// characters that aren't on a swappable key (digits, spaces, most punctuation) pass through.
    /// The map is a bijection over those keys, so one pass handles both Latin→Cyrillic and the
    /// reverse — callers don't need to know which layout the text was typed in.
    /// </summary>
    public static string Swap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = Map.TryGetValue(src[i], out var mapped) ? mapped : src[i];
        });
    }

    /// <summary>
    /// True if <paramref name="candidate"/> contains <paramref name="query"/> under either keyboard
    /// layout — the query as typed, or its layout-swapped form. An empty query matches everything.
    /// Both arguments should already be normalized (see <see cref="TagNormalizer.Normalize"/>), since
    /// the swap table is keyed on lowercase glyphs. This is the single definition of layout-tolerant
    /// matching, shared by the tag-filter box and the add-tag autocomplete so they behave alike.
    /// </summary>
    public static bool MatchesEitherLayout(string candidate, string query)
    {
        if (query.Length == 0) return true;
        if (candidate.Contains(query, StringComparison.Ordinal)) return true;
        var swapped = Swap(query);
        return swapped != query && candidate.Contains(swapped, StringComparison.Ordinal);
    }

    private static Dictionary<char, char> BuildMap()
    {
        var map = new Dictionary<char, char>(Qwerty.Length * 2);
        for (var i = 0; i < Qwerty.Length; i++)
        {
            map[Qwerty[i]] = Russian[i];
            map[Russian[i]] = Qwerty[i];
        }
        return map;
    }
}
