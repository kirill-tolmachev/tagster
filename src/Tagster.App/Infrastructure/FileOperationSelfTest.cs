using System.IO;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>
/// Headless check (invoked via <c>--fileop-test</c>) that drives the real shell
/// <see cref="IFileOperationService"/> through a create/move/copy/rename/delete round-trip on temp
/// folders — the only way to verify the COM <c>IFileOperation</c> vtable end to end without a human.
/// Operations run on tiny non-colliding temp items, so no shell progress dialog or prompt appears.
/// </summary>
internal static class FileOperationSelfTest
{
    public static (bool Ok, string Message) Run(IFileOperationService ops)
    {
        var root = Path.Combine(Path.GetTempPath(), "tagster-fileop-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // CreateFolder, including Explorer-style auto-suffixing on collision.
            var first = ops.CreateFolder(root, "box");
            var second = ops.CreateFolder(root, "box");
            if (Path.GetFileName(first) != "box") return (false, $"FAIL: first folder named '{Path.GetFileName(first)}'");
            if (Path.GetFileName(second) != "box (2)") return (false, $"FAIL: suffix folder named '{Path.GetFileName(second)}'");

            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "hello.txt"), "hi");

            // Move src into box.
            if (ops.Move([src], first, 0) != FileOpResult.Completed) return (false, "FAIL: Move returned cancelled");
            var movedFile = Path.Combine(first, "src", "hello.txt");
            if (Directory.Exists(src)) return (false, "FAIL: source still present after Move");
            if (!File.Exists(movedFile)) return (false, "FAIL: file missing after Move");

            // Copy box\src back out to the root.
            if (ops.Copy([Path.Combine(first, "src")], root, 0) != FileOpResult.Completed) return (false, "FAIL: Copy returned cancelled");
            if (!File.Exists(Path.Combine(root, "src", "hello.txt"))) return (false, "FAIL: file missing after Copy");
            if (!File.Exists(movedFile)) return (false, "FAIL: original lost after Copy");

            // Rename root\src -> renamed.
            var renamed = ops.Rename(Path.Combine(root, "src"), "renamed", 0);
            if (renamed is null) return (false, "FAIL: Rename returned cancelled");
            if (!Directory.Exists(Path.Combine(root, "renamed"))) return (false, "FAIL: renamed folder missing");
            if (Directory.Exists(Path.Combine(root, "src"))) return (false, "FAIL: old name still present after Rename");

            // Delete everything to the Recycle Bin.
            if (ops.Delete([first, second, Path.Combine(root, "renamed")], 0) != FileOpResult.Completed) return (false, "FAIL: Delete returned cancelled");
            if (Directory.Exists(first) || Directory.Exists(second) || Directory.Exists(Path.Combine(root, "renamed")))
                return (false, "FAIL: items still present after Delete");

            return (true, "PASS: create/move/copy/rename/delete round-trip via IFileOperation works");
        }
        catch (Exception ex)
        {
            return (false, "FAIL: " + ex);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
