using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GhstRDP.Core;

/// <summary>Imagens de banner dos cards (logo da empresa). Ficam em %APPDATA%\GhstRDP\banners.</summary>
public static class Banners
{
    public const string FileFilter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*";
    const long MaxBytes = 15 * 1024 * 1024;

    public static string? PathFor(ServerRecord record) =>
        string.IsNullOrEmpty(record.BannerFile) ? null : Path.Combine(Paths.BannersDir, record.BannerFile);

    /// <summary>Carrega sem travar o arquivo (pode ser trocado/apagado depois).</summary>
    public static ImageSource? Load(string? path, int decodeWidth = 640)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir a imagem {path}", ex);
            return null;
        }
    }

    /// <summary>Imagem bem mais larga que alta (banner pronto, ex.: 2:1): deve cobrir o card todo.</summary>
    public static bool IsWide(ImageSource? image) =>
        image is BitmapSource b && b.PixelHeight > 0 && (double)b.PixelWidth / b.PixelHeight >= 1.5;

    /// <summary>Confere se o arquivo é uma imagem que o app consegue abrir.</summary>
    public static string? Validate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "Arquivo não encontrado.";
            if (info.Length > MaxBytes) return "Imagem muito grande (máximo 15 MB).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        return Load(path, 64) == null ? "Esse arquivo não é uma imagem que o Windows consegue abrir." : null;
    }

    /// <summary>Copia a imagem para a pasta de banners com o id do servidor e devolve o nome do arquivo.</summary>
    public static string Save(Guid serverId, string sourcePath)
    {
        Directory.CreateDirectory(Paths.BannersDir);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || ext.Length > 6) ext = ".png";
        var name = $"{serverId}{ext}";
        var target = Path.Combine(Paths.BannersDir, name);

        // Remove versões antigas com outra extensão
        foreach (var old in Directory.GetFiles(Paths.BannersDir, $"{serverId}.*"))
        {
            if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                TryDelete(old);
        }

        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var tmp = target + ".tmp";
            File.Copy(sourcePath, tmp, true);
            File.Move(tmp, target, true);
        }
        return name;
    }

    public static void Delete(ServerRecord record)
    {
        var path = PathFor(record);
        if (path != null) TryDelete(path);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { Log.Warn($"Falha ao apagar {path}", ex); }
    }
}
