namespace GhstRDP.Core;

public enum ServerKind { Vps, Dedicado, Outro }

/// <summary>Como a conexão abre. <see cref="Padrao"/> usa o padrão definido nas configurações.</summary>
public enum OpenMode { Padrao, Embutido, Externo }

public enum DisplayMode { AjustarJanela, TelaCheia, Personalizada }

/// <summary>Valores iguais ao AudioRedirectionMode do controle RDP.</summary>
public enum AudioMode { NestePC = 0, NoServidor = 1, Desligado = 2 }

/// <summary>Valores iguais ao KeyboardHookMode do controle RDP.</summary>
public enum WinKeysMode { Nunca = 0, Sempre = 1, SomenteTelaCheia = 2 }

public sealed class ServerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";

    /// <summary>Senha criptografada com DPAPI (usuário atual do Windows). Nunca em texto puro.</summary>
    public string? PasswordProtected { get; set; }

    public ServerKind Kind { get; set; } = ServerKind.Vps;
    public string Group { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }

    public OpenMode OpenMode { get; set; } = OpenMode.Padrao;
    public DisplayMode Display { get; set; } = DisplayMode.AjustarJanela;
    public int CustomWidth { get; set; } = 1920;
    public int CustomHeight { get; set; } = 1080;
    public bool Multimon { get; set; }

    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public AudioMode Audio { get; set; } = AudioMode.NestePC;
    public bool AdminSession { get; set; }

    /// <summary>Imagem do card (ex.: logo da empresa), guardada em banners\. Tem prioridade sobre a prévia da sessão.</summary>
    public string? BannerFile { get; set; }
    /// <summary>true = a imagem cobre o card todo; false = logo centralizada.</summary>
    public bool BannerFill { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastConnectedAt { get; set; }
    public int ConnectCount { get; set; }

    public ServerRecord Clone() => (ServerRecord)MemberwiseClone();
}

public sealed class AppSettings
{
    public OpenMode DefaultOpenMode { get; set; } = OpenMode.Embutido;
    public WinKeysMode WinKeys { get; set; } = WinKeysMode.SomenteTelaCheia;
    public bool MatchWindowsScale { get; set; } = true;
    public bool StatusCheck { get; set; } = true;
    public int StatusIntervalSeconds { get; set; } = 30;
    public bool Thumbnails { get; set; } = true;
    public bool ListView { get; set; }
    public bool NavCollapsed { get; set; }

    /// <summary>Modo sigiloso: esconde IPs na tela (menos na edição) e pixeliza as prévias.</summary>
    public bool PrivacyMode { get; set; }

    /// <summary>Baixa e instala novas versões do GitHub sozinho (troca o .exe ao fechar).</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Cor de cada cliente (nome em minúsculas → índice da paleta). Fica fixa depois de escolhida.</summary>
    public Dictionary<string, int> ClientColors { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<ServerRecord> Servers { get; set; } = new();
}
