using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GhstRDP.Core;

/// <summary>Card banner images (usually a client logo), stored in <see cref="Paths.BannersDir"/>.</summary>
public static class Banners
{
    public const string FileFilter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*";
    const long MaxBytes = 15 * 1024 * 1024;

    public static string? PathFor(ServerRecord record) =>
        string.IsNullOrEmpty(record.BannerFile) ? null : Path.Combine(Paths.BannersDir, Path.GetFileName(record.BannerFile));

    /// <summary>Loads an image without keeping the file locked.</summary>
    /// <returns>A frozen image, or null when the file is missing or unreadable.</returns>
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
            Log.Warn($"Could not open image {path}", ex);
            return null;
        }
    }

    /// <returns>True for banner-shaped images (1.5:1 or wider), which should cover the whole card.</returns>
    public static bool IsWide(ImageSource? image) =>
        image is BitmapSource b && b.PixelHeight > 0 && (double)b.PixelWidth / b.PixelHeight >= 1.5;

    /// <returns>An error message for the user, or null when the file is a usable image.</returns>
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

    /// <summary>Copies an image into the banners folder, named after the server.</summary>
    /// <returns>The stored file name, to put in <see cref="ServerRecord.BannerFile"/>.</returns>
    public static string Save(Guid serverId, string sourcePath)
    {
        Directory.CreateDirectory(Paths.BannersDir);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || ext.Length > 6) ext = ".png";
        var name = $"{serverId}{ext}";
        var target = Path.Combine(Paths.BannersDir, name);

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
        catch (Exception ex) { Log.Warn($"Failed to delete {path}", ex); }
    }
}
