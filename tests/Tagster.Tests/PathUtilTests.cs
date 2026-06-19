using Tagster.Core;

namespace Tagster.Tests;

public class PathUtilTests
{
    [Theory]
    [InlineData(@"C:\Archive", @"C:\Archive\Ivanov", true)]
    [InlineData(@"C:\Archive", @"C:\Archive", true)]
    [InlineData(@"C:\Archive", @"C:\Other\Ivanov", false)]
    [InlineData(@"C:\Archive", @"C:\ArchiveX\Ivanov", false)]
    public void IsUnderRoot_distinguishes_descendants(string root, string path, bool expected)
        => Assert.Equal(expected, PathUtil.IsUnderRoot(root, path));
}
