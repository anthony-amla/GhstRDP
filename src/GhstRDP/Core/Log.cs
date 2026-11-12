using System.IO;
using System.Text;

namespace GhstRDP.Core;

/// <summary>Daily file log. Never throws.</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static string CurrentFile => Path.Combine(Paths.LogsDir, $"ghst-rdp-{DateTime.Now:yyyyMMdd}.log");

    static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(message);
            if (ex != null) sb.AppendLine().Append(ex);
            sb.AppendLine();
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.LogsDir);
                File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
