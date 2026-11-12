using System.IO;
using System.Text;

namespace GhstRDP.Core;

public static class AtomicFile
{
    /// <summary>Writes to a temp file and swaps it in, keeping the previous version as a backup.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="content">Text to write (UTF-8, no BOM).</param>
    /// <param name="backupPath">Where the previous version of <paramref name="path"/> is kept.</param>
    public static void Write(string path, string content, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            writer.Write(content);
        }

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }

        try
        {
            File.Replace(tmp, path, backupPath, ignoreMetadataErrors: true);
        }
        catch (IOException)
        {
            File.Copy(path, backupPath, true);
            File.Move(tmp, path, true);
        }
    }
}
