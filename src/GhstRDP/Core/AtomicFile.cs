using System.IO;
using System.Text;

namespace GhstRDP.Core;

/// <summary>Gravação atômica: escreve num .tmp e troca de uma vez, guardando o anterior como backup.</summary>
public static class AtomicFile
{
    public static void Write(string path, string content, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            writer.Write(content);
        }

        if (File.Exists(path))
        {
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
        else
        {
            File.Move(tmp, path);
        }
    }
}
