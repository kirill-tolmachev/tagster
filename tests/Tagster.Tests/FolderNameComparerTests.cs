using Tagster.Core;

namespace Tagster.Tests;

public class FolderNameComparerTests
{
    private static string[] Sorted(params string[] names)
    {
        Array.Sort(names, FolderNameComparer.Default);
        return names;
    }

    [Fact]
    public void Latin_names_sort_above_cyrillic()
    {
        Assert.Equal(
            new[] { "Adams", "Smith", "Иванов", "Сидоров" },
            Sorted("Сидоров", "Smith", "Иванов", "Adams"));
    }

    [Fact]
    public void Letterless_names_sort_first()
    {
        // No Latin or Cyrillic letter at all → the leading "other" group, above Latin and Cyrillic.
        Assert.Equal(
            new[] { "2024", "Adams", "Иванов" },
            Sorted("Иванов", "Adams", "2024"));
    }

    [Fact]
    public void First_letter_decides_the_group_skipping_leading_digits()
    {
        // "12 Иванов" → first letter is Cyrillic; "07 Adams" → first letter is Latin.
        Assert.Equal(
            new[] { "07 Adams", "12 Иванов" },
            Sorted("12 Иванов", "07 Adams"));
    }

    [Fact]
    public void Date_prefixed_latin_name_joins_the_latin_group_not_the_letterless_one()
    {
        // "2024-08 Smith" has a Latin first letter, so it ranks with Latin (ahead of Cyrillic) — it is
        // not treated as letterless despite the leading digits.
        Assert.Equal(
            new[] { "2024-08 Smith", "Иванов" },
            Sorted("Иванов", "2024-08 Smith"));
    }

    [Fact]
    public void Within_a_group_order_is_case_insensitive()
    {
        Assert.Equal(
            new[] { "Adams", "BROWN", "smith" },
            Sorted("smith", "BROWN", "Adams"));
    }
}
