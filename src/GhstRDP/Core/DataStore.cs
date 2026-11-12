using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GhstRDP.Core;

/// <summary>Loads and saves data.json, falling back to the backup when the main file is corrupt.</summary>
public sealed class DataStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly object _gate = new();

    public AppData Data { get; private set; } = new();

    /// <summary>Message for the user when data had to be recovered on load, otherwise null.</summary>
    public string? Warning { get; private set; }

    public void Load()
    {
        Paths.EnsureCreated();

        if (TryRead(Paths.DataFile, out var data, out var upgraded))
        {
            Data = data;
            if (upgraded) Save();
            return;
        }

        var mainExists = File.Exists(Paths.DataFile);
        if (mainExists)
        {
            try
            {
                File.Copy(Paths.DataFile, Paths.DataFile + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", true);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not keep a copy of the corrupt data file", ex);
            }
        }

        if (TryRead(Paths.DataBackupFile, out data, out _))
        {
            Data = data;
            Warning = "O arquivo de dados estava danificado. Restaurei automaticamente o último backup.";
            Save();
            return;
        }

        if (mainExists)
            Warning = "O arquivo de dados estava danificado e não havia backup válido. Uma cópia foi guardada na pasta de dados.";

        Data = new AppData();
    }

    static bool TryRead(string path, out AppData data, out bool upgraded)
    {
        data = null!;
        upgraded = false;
        try
        {
            if (!File.Exists(path)) return false;
            if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is not JsonObject root) return false;

            upgraded = DataMigration.Upgrade(root);
            var parsed = root.Deserialize<AppData>(Json);
            if (parsed == null) return false;

            parsed.Settings ??= new AppSettings();
            parsed.Settings.ClientColors ??= new Dictionary<string, int>();
            parsed.Servers ??= new List<ServerRecord>();
            data = parsed;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read {path}", ex);
            return false;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            Paths.EnsureCreated();
            AtomicFile.Write(Paths.DataFile, JsonSerializer.Serialize(Data, Json), Paths.DataBackupFile);
        }
    }
}
