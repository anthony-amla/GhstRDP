using System.Diagnostics;
using System.IO;
using System.Text;

namespace GhstRDP.Core;

/// <summary>Modo "abrir": gera um .rdp, deixa a senha no Gerenciador de Credenciais e abre o mstsc.</summary>
public static class MstscLauncher
{
    public static string FullUser(ServerRecord s) =>
        string.IsNullOrWhiteSpace(s.Domain) ? s.Username.Trim() : $"{s.Domain.Trim()}\\{s.Username.Trim()}";

    public static void Launch(ServerRecord s, string? password, AppSettings settings)
    {
        if (!string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(s.Username))
            CredentialStore.WriteRdp(s.Host, FullUser(s), password);

        Directory.CreateDirectory(Paths.RdpDir);
        var file = Path.Combine(Paths.RdpDir, $"{s.Id}.rdp");
        // mstsc lê .rdp em UTF-16 LE com BOM
        File.WriteAllText(file, BuildRdp(s, settings), Encoding.Unicode);

        var mstsc = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
        Process.Start(new ProcessStartInfo(mstsc, $"\"{file}\"") { UseShellExecute = false });
    }

    public static string BuildRdp(ServerRecord s, AppSettings settings)
    {
        var sb = new StringBuilder();
        void L(string line) => sb.Append(line).Append("\r\n");

        L($"full address:s:{(s.Port == 3389 ? s.Host : $"{s.Host}:{s.Port}")}");
        if (!string.IsNullOrWhiteSpace(s.Username)) L($"username:s:{FullUser(s)}");

        switch (s.Display)
        {
            case DisplayMode.TelaCheia:
                L("screen mode id:i:2");
                break;
            case DisplayMode.Personalizada:
                L("screen mode id:i:1");
                L($"desktopwidth:i:{s.CustomWidth}");
                L($"desktopheight:i:{s.CustomHeight}");
                break;
            default:
                var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1600, 900);
                L("screen mode id:i:1");
                L($"desktopwidth:i:{(int)(area.Width * 0.85) & ~1}");
                L($"desktopheight:i:{(int)(area.Height * 0.85)}");
                break;
        }

        L($"use multimon:i:{(s.Multimon ? 1 : 0)}");
        L("dynamic resolution:i:1");
        L("smart sizing:i:1");
        L("session bpp:i:32");
        L("compression:i:1");
        L($"keyboardhook:i:{(int)settings.WinKeys}");
        L($"audiomode:i:{(int)s.Audio}");
        L($"redirectclipboard:i:{(s.RedirectClipboard ? 1 : 0)}");
        L($"redirectprinters:i:{(s.RedirectPrinters ? 1 : 0)}");
        L($"drivestoredirect:s:{(s.RedirectDrives ? "*" : "")}");
        L("redirectsmartcards:i:0");
        L("redirectcomports:i:0");
        L($"administrative session:i:{(s.AdminSession ? 1 : 0)}");
        L("autoreconnection enabled:i:1");
        L("autoreconnect max retries:i:20");
        L("authentication level:i:2");
        L("prompt for credentials:i:0");
        L("promptcredentialonce:i:1");
        L("negotiate security layer:i:1");
        L("enablecredsspsupport:i:1");
        L("connection type:i:7");
        L("networkautodetect:i:1");
        L("bandwidthautodetect:i:1");
        L("displayconnectionbar:i:1");
        L("bitmapcachepersistenable:i:1");
        return sb.ToString();
    }

    public static void DeleteRdpFile(Guid id)
    {
        try { File.Delete(Path.Combine(Paths.RdpDir, $"{id}.rdp")); } catch { /* ignore */ }
    }
}
