using System.Windows.Media;

namespace GhstRDP.Core;

/// <summary>Client tag colors: a soft tinted background with light text in the same hue.</summary>
public static class TagPalette
{
    static readonly (Color Text, Color Base)[] Colors =
    {
        (Rgb(0x93, 0xC5, 0xFD), Rgb(0x60, 0xA5, 0xFA)), // blue
        (Rgb(0x86, 0xEF, 0xAC), Rgb(0x4A, 0xDE, 0x80)), // green
        (Rgb(0xFC, 0xD3, 0x4D), Rgb(0xFB, 0xBF, 0x24)), // amber
        (Rgb(0xF9, 0xA8, 0xD4), Rgb(0xF4, 0x72, 0xB6)), // pink
        (Rgb(0xC4, 0xB5, 0xFD), Rgb(0xA7, 0x8B, 0xFA)), // violet
        (Rgb(0x67, 0xE8, 0xF9), Rgb(0x22, 0xD3, 0xEE)), // cyan
        (Rgb(0xFD, 0xBA, 0x74), Rgb(0xFB, 0x92, 0x3C)), // orange
        (Rgb(0xFC, 0xA5, 0xA5), Rgb(0xF8, 0x71, 0x71)), // red
        (Rgb(0xBE, 0xF2, 0x64), Rgb(0xA3, 0xE6, 0x35)), // lime
        (Rgb(0x5E, 0xEA, 0xD4), Rgb(0x2D, 0xD4, 0xBF)), // teal
    };

    public static int Count => Colors.Length;

    static readonly Brush[] Backgrounds = Colors.Select(c => Freeze(new SolidColorBrush(Color.FromArgb(0x2B, c.Base.R, c.Base.G, c.Base.B)))).ToArray();
    static readonly Brush[] Foregrounds = Colors.Select(c => Freeze(new SolidColorBrush(c.Text))).ToArray();
    static readonly Brush[] Dots = Colors.Select(c => Freeze(new SolidColorBrush(c.Base))).ToArray();

    public static Brush Background(int index) => Backgrounds[Wrap(index)];
    public static Brush Foreground(int index) => Foregrounds[Wrap(index)];
    public static Brush Dot(int index) => Dots[Wrap(index)];

    static int Wrap(int index) => ((index % Colors.Length) + Colors.Length) % Colors.Length;
    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <returns>A stable palette index for <paramref name="key"/> (FNV-1a), used as the first choice for new clients.</returns>
    public static int HashIndex(string key)
    {
        uint hash = 2166136261;
        foreach (var ch in key) hash = (hash ^ ch) * 16777619;
        return (int)(hash % (uint)Colors.Length);
    }
}
