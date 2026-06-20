namespace Tagster.App;

/// <summary>Parses Tagster's activation arguments (used by the Explorer context menu).</summary>
internal static class CommandLine
{
    public static (string? Folder, bool Edit) Parse(IReadOnlyList<string> args)
    {
        string? folder = null;
        var edit = false;

        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                folder = args[++i];
            else if (string.Equals(args[i], "--edit", StringComparison.OrdinalIgnoreCase))
                edit = true;
        }

        return (folder, edit);
    }
}
