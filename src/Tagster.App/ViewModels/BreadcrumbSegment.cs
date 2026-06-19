namespace Tagster.App;

/// <summary>One clickable segment of the address breadcrumb.</summary>
public sealed class BreadcrumbSegment(string name, string fullPath)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
}
