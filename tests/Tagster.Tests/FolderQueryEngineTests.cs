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

    private static IEnumerable<string> Names(SearchQuery q)
        => FolderQueryEngine.Filter(Sample, q).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal);
}
