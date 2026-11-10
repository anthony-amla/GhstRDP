using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace GhstRDP.Core;

/// <summary>
/// Prévia da última tela da sessão, salva só neste PC.
/// Nunca captura o que estiver por cima da sessão: se não der para garantir que a área
/// visível é a própria sessão, simplesmente não gera prévia.
/// </summary>
public static class Thumbnails
{
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);

    const uint PW_RENDERFULLCONTENT = 2;
    const uint GA_ROOT = 2;

    public static string PathFor(Guid id) => Path.Combine(Paths.ThumbsDir, $"{id}.jpg");

    public static bool Capture(WinForms.Control control, Guid id)
    {
        int w = control.Width, h = control.Height;
        if (w < 200 || h < 150 || !control.IsHandleCreated) return false;

        using var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try { PrintWindow(control.Handle, hdc, PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
        }

        if (IsMostlyBlack(full))
        {
            // Cópia da tela só se a sessão estiver na frente e sem nada por cima
            if (!IsUnobstructed(control)) return false;
            using var g = Graphics.FromImage(full);
            g.CopyFromScreen(control.PointToScreen(Point.Empty), Point.Empty, new Size(w, h));
            if (IsMostlyBlack(full)) return false;
        }

        const int tw = 480;
        var th = (int)Math.Round(h * (tw / (double)w));
        using var thumb = new Bitmap(tw, th, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(full, 0, 0, tw, th);
        }

        Directory.CreateDirectory(Paths.ThumbsDir);
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 82L);
        var target = PathFor(id);
        var tmp = target + ".tmp";
        thumb.Save(tmp, codec, parameters);
        File.Move(tmp, target, true);
        return true;
    }

    /// <summary>A janela do app está em primeiro plano e todos os pontos da área pertencem a ela?</summary>
    static bool IsUnobstructed(WinForms.Control control)
    {
        var root = GetAncestor(control.Handle, GA_ROOT);
        if (root == IntPtr.Zero || IsIconic(root) || GetForegroundWindow() != root) return false;

        var origin = control.PointToScreen(Point.Empty);
        for (var i = 1; i <= 5; i++)
        for (var j = 1; j <= 5; j++)
        {
            var p = new POINT { X = origin.X + control.Width * i / 6, Y = origin.Y + control.Height * j / 6 };
            var hit = WindowFromPoint(p);
            if (hit == IntPtr.Zero || GetAncestor(hit, GA_ROOT) != root) return false;
        }
        return true;
    }

    static bool IsMostlyBlack(Bitmap bmp)
    {
        int bright = 0, samples = 0;
        for (int y = bmp.Height / 12; y < bmp.Height; y += Math.Max(1, bmp.Height / 12))
        for (int x = bmp.Width / 12; x < bmp.Width; x += Math.Max(1, bmp.Width / 12))
        {
            var c = bmp.GetPixel(x, y);
            samples++;
            if (c.R + c.G + c.B > 40) bright++;
        }
        return samples == 0 || bright < samples / 10;
    }

    public static void Delete(Guid id)
    {
        try { File.Delete(PathFor(id)); } catch { /* ignore */ }
    }
}
