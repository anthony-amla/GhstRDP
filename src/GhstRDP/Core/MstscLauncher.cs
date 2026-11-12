using System.Diagnostics;
using System.IO;
using System.Text;

namespace GhstRDP.Core;

/// <summary>Opens a server in the classic mstsc window through a generated .rdp file.</summary>
public static class MstscLauncher
{
    /// <returns>DOMAIN\user when a domain is set, otherwise just the user name.</returns>
    public static string FullUser(ServerRecord server) =>
        string.IsNullOrWhiteSpace(server.Domain)
            ? server.Username.Trim()
            : $"{server.Domain.Trim()}\\{server.Username.Trim()}";

    /// <param name="password">Saved to Credential Manager first so mstsc logs in without prompting. Optional.</param>
    public static void Launch(ServerRecord server, string? password, AppSettings settings)
    {
        if (!string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(server.Username))
            CredentialStore.WriteRdp(server.Host, FullUser(server), password);

        Directory.CreateDirectory(Paths.RdpDir);
        var file = Path.Combine(Paths.RdpDir, $"{server.Id}.rdp");
        // mstsc expects .rdp files as UTF-16 LE with BOM.
        File.WriteAllText(file, BuildRdp(server, settings), Encoding.Unicode);

        var mstsc = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
        Process.Start(new ProcessStartInfo(mstsc, $"\"{file}\"") { UseShellExecute = false });
    }

    public static string BuildRdp(ServerRecord server, AppSettings settings)
    {
        var sb = new StringBuilder();
        void Line(string line) => sb.Append(line).Append("\r\n");

        Line($"full address:s:{(server.Port == 3389 ? server.Host : $"{server.Host}:{server.Port}")}");
        if (!string.IsNullOrWhiteSpace(server.Username)) Line($"username:s:{FullUser(server)}");

        switch (server.Display)
        {
            case DisplayMode.FullScreen:
                Line("screen mode id:i:2");
                break;
            case DisplayMode.Custom:
                Line("screen mode id:i:1");
                Line($"desktopwidth:i:{server.CustomWidth}");
                Line($"desktopheight:i:{server.CustomHeight}");
                break;
            default:
                var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1600, 900);
                Line("screen mode id:i:1");
                Line($"desktopwidth:i:{(int)(area.Width * 0.85) & ~1}");
                Line($"desktopheight:i:{(int)(area.Height * 0.85)}");
                break;
        }

        Line($"use multimon:i:{(server.Multimon ? 1 : 0)}");
        Line("dynamic resolution:i:1");
        Line("smart sizing:i:1");
        Line("session bpp:i:32");
        Line("compression:i:1");
        Line($"keyboardhook:i:{(int)settings.WinKeys}");
        Line($"audiomode:i:{(int)server.Audio}");
        Line($"redirectclipboard:i:{(server.RedirectClipboard ? 1 : 0)}");
        Line($"redirectprinters:i:{(server.RedirectPrinters ? 1 : 0)}");
        Line($"drivestoredirect:s:{(server.RedirectDrives ? "*" : "")}");
        Line("redirectsmartcards:i:0");
        Line("redirectcomports:i:0");
        Line($"administrative session:i:{(server.AdminSession ? 1 : 0)}");
        Line("autoreconnection enabled:i:1");
        Line("autoreconnect max retries:i:20");
        Line("authentication level:i:2");
        Line("prompt for credentials:i:0");
        Line("promptcredentialonce:i:1");
        Line("negotiate security layer:i:1");
        Line("enablecredsspsupport:i:1");
        Line("connection type:i:7");
        Line("networkautodetect:i:1");
        Line("bandwidthautodetect:i:1");
        Line("displayconnectionbar:i:1");
        Line("bitmapcachepersistenable:i:1");
        return sb.ToString();
    }

    public static void DeleteRdpFile(Guid id)
    {
        try { File.Delete(Path.Combine(Paths.RdpDir, $"{id}.rdp")); }
        catch (Exception ex) { Log.Warn("Failed to delete .rdp file", ex); }
    }
}
