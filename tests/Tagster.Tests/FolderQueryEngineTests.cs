using Tagster.Core;

namespace Tagster.Tests;

public class FolderQueryEngineTests
{
    private static TaggedFolder Folder(string name, params string[] tags) => new()
    {
        Id = Guid.NewGuid(),
        RootPath = @"C:\root",
        RelativePath = name,
        Name = name,
        Tags = tags,
    };

    private static readonly TaggedFolder[] Sample =
    [
        Folder("Иванов", "док", "война"),
        Folder("Петров", "док", "портрет"),
        Folder("Сидоров", "портрет"),
        Folder("Smith", "doc"),
    ];

    [Fact]
    public void Include_all_requires_every_tag()
    {
        var q = new SearchQuery { Include = ["док", "портрет"], IncludeMatch = TagMatch.All };
        Assert.Equal(new[] { "Петров" }, Names(q));
    }

    [Fact]
    public void Include_any_requires_at_least_one()
    {
        var q = new SearchQuery { Include = ["война", "портрет"], IncludeMatch = TagMatch.Any };
        Assert.Equal(new[] { "Иванов", "Петров", "Сидоров" }, Names(q));
    }

    [Fact]
    public void Exclude_removes_folders_carrying_the_excluded_tag()
    {
        // has "док" but NOT "война" — the requested real-world query
        var q = new SearchQuery { Include = ["док"], Exclude = ["война"] };
        Assert.Equal(new[] { "Петров" }, Names(q));
    }

    [Fact]
    public void Matching_is_case_insensitive_for_cyrillic()
    {
        var q = new SearchQuery { Include = ["ДОК"] };
        Assert.Equal(new[] { "Иванов", "Петров" }, Names(q));
    }

    [Fact]
    public void Name_filter_is_case_insensitive_substring()
    {
        var q = new SearchQuery { NameContains = "ноВ" };
        Assert.Equal(new[] { "Иванов" }, Names(q));
    }

    [Fact]
    public void CoOccurring_tags_are_those_carried_by_the_matches()
    {
        // Selecting "док" matches Иванов(док,война) and Петров(док,портрет): the tags available to
        // combine next are exactly док/война/портрет. The Latin "doc" never co-occurs, so it drops.
        var matches = FolderQueryEngine.Filter(Sample, new SearchQuery { Include = ["док"] });
        var counts = FolderQueryEngine.CoOccurringTagCounts(matches);

        Assert.Equal(new[] { "война", "док", "портрет" }, counts.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(2, counts["док"]);
        Assert.Equal(1, counts["война"]);
        Assert.Equal(1, counts["портрет"]);
        Assert.False(counts.ContainsKey("doc"));
    }

    [Fact]
    public void CoOccurring_keys_are_normalized_and_counted_per_folder()
    {
        // Mixed case + a duplicate within one folder must still count that folder once.
        var folders = new[] { Folder("A", "Док", "док"), Folder("B", "ДОК") };
        var counts = FolderQueryEngine.CoOccurringTagCounts(folders);

        Assert.Equal(new[] { "док" }, counts.Keys);
        Assert.Equal(2, counts["док"]);
    }

    private static IEnumerable<string> Names(SearchQuery q)
        => FolderQueryEngine.Filter(Sample, q).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal);
}
