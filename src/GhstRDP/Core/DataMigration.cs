using System.Text.Json.Nodes;

namespace GhstRDP.Core;

public static class DataMigration
{
    public const int CurrentVersion = 2;

    static readonly Dictionary<string, string> ServerKind = new() { ["Dedicado"] = "Dedicated", ["Outro"] = "Other" };
    static readonly Dictionary<string, string> OpenMode = new() { ["Padrao"] = "Default", ["Embutido"] = "Embedded", ["Externo"] = "External" };
    static readonly Dictionary<string, string> DisplayMode = new() { ["AjustarJanela"] = "FitWindow", ["TelaCheia"] = "FullScreen", ["Personalizada"] = "Custom" };
    static readonly Dictionary<string, string> AudioMode = new() { ["NestePC"] = "Local", ["NoServidor"] = "Remote", ["Desligado"] = "Off" };
    static readonly Dictionary<string, string> WinKeysMode = new() { ["Nunca"] = "Never", ["Sempre"] = "Always", ["SomenteTelaCheia"] = "FullScreenOnly" };

    /// <summary>Upgrades a raw data.json document in place to <see cref="CurrentVersion"/>.</summary>
    /// <param name="root">Parsed data.json root object.</param>
    /// <returns>True when anything was changed.</returns>
    public static bool Upgrade(JsonObject root)
    {
        var version = root["Version"]?.GetValue<int>() ?? 1;
        if (version >= CurrentVersion) return false;

        if (root["Settings"] is JsonObject settings)
        {
            Rename(settings, "DefaultOpenMode", OpenMode);
            Rename(settings, "WinKeys", WinKeysMode);
        }

        if (root["Servers"] is JsonArray servers)
        {
            foreach (var server in servers.OfType<JsonObject>())
            {
                Rename(server, "Kind", ServerKind);
                Rename(server, "OpenMode", OpenMode);
                Rename(server, "Display", DisplayMode);
                Rename(server, "Audio", AudioMode);
            }
        }

        root["Version"] = CurrentVersion;
        return true;
    }

    static void Rename(JsonObject obj, string property, Dictionary<string, string> names)
    {
        if (obj[property] is JsonValue value && value.TryGetValue(out string? name) && names.TryGetValue(name, out var renamed))
            obj[property] = renamed;
    }
}
