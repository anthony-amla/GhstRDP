using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhstRDP.Core;

/// <summary>
/// Persistência em JSON com gravação atômica (arquivo temporário + replace) e backup automático.
/// Se o arquivo principal corromper, o backup é restaurado.
/// </summary>
public sealed class DataStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly object _gate = new();

    public AppData Data { get; private set; } = new();

    /// <summary>Aviso para o usuário quando houve recuperação de backup.</summary>
    public string? Warning { get; private set; }

    public void Load()
    {
        Paths.EnsureCreated();

        if (TryRead(Paths.DataFile, out var data))
        {
            Data = data;
            return;
        }

        var mainExists = File.Exists(Paths.DataFile);
        if (mainExists)
        {
            try
            {
                File.Copy(Paths.DataFile, Paths.DataFile + $".corrompido-{DateTime.Now:yyyyMMdd-HHmmss}", true);
            }
            catch (Exception ex)
            {
                Log.Warn("Não consegui guardar cópia do arquivo corrompido", ex);
            }
        }

        if (TryRead(Paths.DataBackupFile, out data))
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

    static bool TryRead(string path, out AppData data)
    {
        data = null!;
        try
        {
            if (!File.Exists(path)) return false;
            var text = File.ReadAllText(path, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<AppData>(text, Json);
            if (parsed == null) return false;
            parsed.Settings ??= new AppSettings();
            parsed.Settings.ClientColors ??= new Dictionary<string, int>();
            parsed.Servers ??= new List<ServerRecord>();
            data = parsed;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao ler {path}", ex);
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
