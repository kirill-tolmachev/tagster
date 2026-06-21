using System.Globalization;

namespace Tagster.Core;

/// <summary>
/// The single definition of folder display order, shared by the browser and the search results so
/// both lists look identical. Folders are grouped by the script of their <em>first letter</em> —
/// letterless names (digits/symbols only) first, then Latin/Western, then Cyrillic — and ordered
/// alphabetically (culture-aware, case-insensitive) within each group.
/// </summary>
/// <remarks>
/// Classification skips leading non-letters, so "2024-08 Smith" is a Latin name (its first letter is
/// 'S') and sorts at the top of the Latin group; only a name with no Latin or Cyrillic letter at all
/// (e.g. "2024", "No.5") falls into the leading "other" group.
/// </remarks>
public sealed class FolderNameComparer : IComparer<string>
{
    public static FolderNameComparer Default { get; } = new();

    public int Compare(string? x, string? y)
    {
        var rank = ScriptRank(x).CompareTo(ScriptRank(y));
        return rank != 0
            ? rank
            : string.Compare(x, y, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
    }

    /// <summary>0 = letterless/other, 1 = Latin/Western, 2 = Cyrillic — decided by the first letter.</summary>
    private static int ScriptRank(string? name)
    {
        if (name is null) return 0;
        foreach (var ch in name)
        {
            if (!char.IsLetter(ch)) continue;
            return IsCyrillic(ch) ? 2 : 1;
        }
        return 0;
    }

    private static bool IsCyrillic(char ch)
        => ch is (>= 'Ѐ' and <= 'ӿ')   // Cyrillic
            or (>= 'Ԁ' and <= 'ԯ')      // Cyrillic Supplement
            or (>= 'ⷠ' and <= 'ⷿ')      // Cyrillic Extended-A
            or (>= 'Ꙁ' and <= 'ꚟ');     // Cyrillic Extended-B
}
